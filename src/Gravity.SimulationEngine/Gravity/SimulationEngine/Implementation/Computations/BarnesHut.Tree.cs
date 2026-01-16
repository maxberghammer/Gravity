using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Gravity.SimulationEngine.Implementation.Computations;

internal sealed partial class BarnesHut
{
	// Highly optimized Barnes–Hut style quadtree
	// - Allocation-light (pooled array-backed nodes)
	// - No collision collection (acceleration-only)
	// - Iterative traversal for CalculateGravity
	public sealed class Tree
	{
		#region Internal types

		// ReSharper disable InconsistentNaming
		private struct Node
		{
			#region Fields

			// For aggregated leaf (depth limit), accumulate weighted COM while inserting
			public double AggMass;
			public Vector2D AggWeightedCom;

			// Leaf payload
			public Body? Body;
			public Vector2D Com;
			public int Count; // number of bodies aggregated in this node

			// Bounds
			public double L,
						  T,
						  R,
						  B;

			// Mass and center of mass
			public double Mass;

			// Children indices (-1 if none)
			public int NW,
					   NE,
					   SW,
					   SE;

			// Cached squared width for traversal criterion
			public double WidthSq;

			#endregion
		}
		// ReSharper disable InconsistentNaming

		#endregion

		#region Fields

		private const double EpsilonSize = 1e-12; // minimal node size to avoid endless subdivision
		private const int MaxDepth = 32; // safety bound for degenerate cases

		// Thread-local stack pool to eliminate contention during parallel CalculateGravity calls
		[ThreadStatic] private static int[]? _threadLocalStack;

		private readonly double _l,
								_t,
								_r,
								_b; // store bounds for Morton

		private readonly int _root;
		private readonly double _theta;
		private Node[] _nodes;
		private long _traversalVisitCount;

		#endregion

		#region Construction

		public Tree(in Vector2D topLeft, in Vector2D bottomRight, double theta, int estimatedBodies)
		{
			_theta = theta;
			_l = topLeft.X;
			_t = topLeft.Y;
			_r = bottomRight.X;
			_b = bottomRight.Y;
			var initialCapacity = Math.Max(1024, Math.Min(estimatedBodies * 4, 1_000_000));
			_nodes = ArrayPool<Node>.Shared.Rent(initialCapacity);
			NodeCount = 0;
			_root = NewNode(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
		}

		public Tree(in Vector2D topLeft, in Vector2D bottomRight, double theta)
			: this(topLeft, bottomRight, theta, 256)
		{
		}

		#endregion

		#region Interface

		public bool CollectDiagnostics { get; set; } = true;

		public long TraversalVisitCount
			=> _traversalVisitCount;

		public int MaxDepthReached { get; private set; }

		public int NodeCount { get; private set; }

		public void AddRange(Body[] entities)
		{
			var width = Math.Max(EpsilonSize, _r - _l);
			var height = Math.Max(EpsilonSize, _b - _t);
			var invW = 1.0 / width;
			var invH = 1.0 / height;
			var n = entities.Length;
			var pool = ArrayPool<(uint key, Body e)>.Shared;
			var arr = pool.Rent(n);

			for(var i = 0; i < n; i++)
			{
				var p = entities[i].Position;
				var nx = Math.Clamp((p.X - _l) * invW, 0.0, 1.0);
				var ny = Math.Clamp((p.Y - _t) * invH, 0.0, 1.0);
				var x = (uint)(nx * uint.MaxValue);
				var y = (uint)(ny * uint.MaxValue);
				arr[i] = (InterleaveBits(x, y), entities[i]);
			}

			Array.Sort(arr, 0, n, Comparer<(uint key, Body e)>.Create((a, b) => a.key.CompareTo(b.key)));
			for(var i = 0; i < n; i++)
				Insert(_root, arr[i].e, 0);
			pool.Return(arr);
		}

		public void ComputeMassDistribution()
		{
			var pool = ArrayPool<(int idx, int state)>.Shared;
			var stack = pool.Rent(128);
			var sp = 0;
			stack[sp++] = (_root, 0);

			while(sp > 0)
			{
				(var idx, var state) = stack[--sp];
				var n = _nodes[idx];
				var hasChildren = n.NW >= 0;

				if(state == 0 && hasChildren)
				{
					if(sp + 5 >= stack.Length)
					{
						var bigger = pool.Rent(stack.Length * 2);
						Array.Copy(stack, bigger, sp);
						pool.Return(stack);
						stack = bigger;
					}

					stack[sp++] = (idx, 1);
					if(n.NW >= 0)
						stack[sp++] = (n.NW, 0);
					if(n.NE >= 0)
						stack[sp++] = (n.NE, 0);
					if(n.SW >= 0)
						stack[sp++] = (n.SW, 0);
					if(n.SE >= 0)
						stack[sp++] = (n.SE, 0);
				}
				else
				{
					// cache width^2 once per node for traversal
					var w = Math.Max(EpsilonSize, n.R - n.L);
					n.WidthSq = w * w;

					if(hasChildren)
					{
						var mass = 0.0;
						var wcom = Vector2D.Zero;
						Accumulate(ref mass, ref wcom, n.NW);
						Accumulate(ref mass, ref wcom, n.NE);
						Accumulate(ref mass, ref wcom, n.SW);
						Accumulate(ref mass, ref wcom, n.SE);
						n.Mass = mass;
						n.Com = mass > 0.0
									? wcom / mass
									: Vector2D.Zero;
						_nodes[idx] = n;
					}
					else
					{
						if(n.Body != null)
						{
							n.Mass = n.Body.m;
							n.Com = n.Body.Position;
							_nodes[idx] = n;
						}
						else if(n.AggMass > 0.0)
						{
							n.Mass = n.AggMass;
							n.Com = n.AggWeightedCom / n.AggMass;
							_nodes[idx] = n;
						}
					}
				}
			}

			pool.Return(stack);
		}

		public Vector2D CalculateGravity(Body e)
		{
			var acc = Vector2D.Zero;

			// Use thread-local stack to eliminate ArrayPool contention across threads
#pragma warning disable S2696 // Instance method intentionally sets ThreadStatic field
			var stack = _threadLocalStack;

			if(stack == null)
			{
				stack = new int[256];
				_threadLocalStack = stack;
			}
#pragma warning restore S2696

			var sp = 0;
			stack[sp++] = _root;
			var thetaSq = _theta * _theta;
			long localVisits = 0;
			var ePos = e.Position; // cache body position

			while(sp > 0)
			{
				var idx = stack[--sp];
				localVisits++;
				ref var n = ref _nodes[idx]; // use ref to avoid struct copy

				var mass = n.Mass;

				if(mass <= 0.0)
					continue;

				// Cache node center of mass
				var com = n.Com;
				var dx = ePos.X - com.X;
				var dy = ePos.Y - com.Y;
				var dist2 = dx * dx + dy * dy;

				if(dist2 <= 0.0)
					continue;

				var hasChildren = n.NW >= 0;
				var isLeaf = !hasChildren && (n.Body != null || n.AggMass > 0.0);

				if(isLeaf || n.WidthSq / dist2 < thetaSq)
				{
					// Fused calculation: avoid separate sqrt and divisions
					var dist = Math.Sqrt(dist2);
					var invLen3 = 1.0 / (dist2 * dist);
					var factor = -IWorld.G * mass * invLen3;
					acc = acc + new Vector2D(factor * dx, factor * dy);
				}
				else
				{
					// Expand children - handle stack growth if needed
					if(sp + 4 >= stack.Length)
					{
#pragma warning disable S2696
						// Grow thread-local stack (rare case for very deep trees)
						var newStack = new int[stack.Length * 2];
						Array.Copy(stack, newStack, sp);
						_threadLocalStack = stack = newStack;
#pragma warning restore S2696
					}

					// Unroll child checks to reduce branching
					var nw = n.NW;
					var ne = n.NE;
					var sw = n.SW;
					var se = n.SE;

					if(nw >= 0)
						stack[sp++] = nw;
					if(ne >= 0)
						stack[sp++] = ne;
					if(sw >= 0)
						stack[sp++] = sw;
					if(se >= 0)
						stack[sp++] = se;
				}
			}

			if(CollectDiagnostics && localVisits != 0)
				Interlocked.Add(ref _traversalVisitCount, localVisits);

			return acc;
		}

		public void Release()
		{
			ArrayPool<Node>.Shared.Return(_nodes);
			_nodes = Array.Empty<Node>();
			NodeCount = 0;
		}

		#endregion

		#region Implementation

		private static uint InterleaveBits(uint x, uint y)
		{
			var xx = Part1By1(x >> 16);
			var yy = Part1By1(y >> 16);

			return (xx << 1) | yy;
		}

		private static uint Part1By1(uint v)
		{
			v &= 0x0000FFFF;
			v = (v | (v << 8)) & 0x00FF00FF;
			v = (v | (v << 4)) & 0x0F0F0F0F;
			v = (v | (v << 2)) & 0x33333333;
			v = (v | (v << 1)) & 0x55555555;

			return v;
		}

		private void EnsureCapacity(int min)
		{
			if(_nodes.Length >= min)
				return;

			var newArr = ArrayPool<Node>.Shared.Rent(Math.Max(min, _nodes.Length * 2));
			Array.Copy(_nodes, newArr, NodeCount);
			ArrayPool<Node>.Shared.Return(_nodes);
			_nodes = newArr;
		}

		private int NewNode(double l, double t, double r, double b)
		{
			var idx = NodeCount;
			EnsureCapacity(idx + 1);
			var n = new Node
					{
						L = l,
						T = t,
						R = r,
						B = b,
						WidthSq = 0.0,
						NW = -1,
						NE = -1,
						SW = -1,
						SE = -1,
						Body = null,
						Count = 0,
						Mass = 0.0,
						Com = default,
						AggMass = 0.0,
						AggWeightedCom = default
					};
			_nodes[idx] = n;
			NodeCount++;

			return idx;
		}

		private void Insert(int idx, Body e, int depth)
		{
			// Iterative insertion with explicit stack to avoid recursion overhead
			var pool = ArrayPool<(int idx, Body body, int depth, int state)>.Shared;
			var stack = pool.Rent(64);
			var sp = 0;
			stack[sp++] = (idx, e, depth, 0);

			while(sp > 0)
			{
				(var currentIdx, var currentBody, var currentDepth, var state) = stack[--sp];

				if(currentDepth > MaxDepthReached)
					MaxDepthReached = currentDepth;

				ref var n = ref _nodes[currentIdx];

				if(state == 0)
				{
					n.Count++;
					var hasChildren = n.NW >= 0;

					if(!hasChildren &&
					   n.Body == null &&
					   n.AggMass > 0.0)
					{
						n.AggMass += currentBody.m;
						n.AggWeightedCom += currentBody.m * currentBody.Position;

						continue;
					}

					if(!hasChildren &&
					   n.Body == null &&
					   n.Count == 1)
					{
						n.Body = currentBody;

						continue;
					}

					if(!hasChildren)
					{
						var widthEdge = Math.Max(EpsilonSize, n.R - n.L);
						var heightEdge = Math.Max(EpsilonSize, n.B - n.T);

						if(currentDepth >= MaxDepth ||
						   (widthEdge <= EpsilonSize && heightEdge <= EpsilonSize))
						{
							if(n.Body != null)
							{
								n.AggMass += n.Body.m;
								n.AggWeightedCom += n.Body.m * n.Body.Position;
								n.Body = null;
							}

							n.AggMass += currentBody.m;
							n.AggWeightedCom += currentBody.m * currentBody.Position;

							continue;
						}

						// Need to subdivide - push current task back with state=1
						if(sp + 2 >= stack.Length)
						{
							var bigger = pool.Rent(stack.Length * 2);
							Array.Copy(stack, bigger, sp);
							pool.Return(stack);
							stack = bigger;
						}

						Subdivide(currentIdx);
						n = ref _nodes[currentIdx]; // refresh ref after subdivide

						if(n.Body != null)
						{
							var prev = n.Body;
							n.Body = null;
							n.Count--;

							var childIdx = SelectChildIdx(ref n, prev.Position);
							stack[sp++] = (childIdx, prev, currentDepth + 1, 0);
						}
					}

					// Push current body insertion into appropriate child
					n = ref _nodes[currentIdx]; // refresh
					var currentChildIdx = SelectChildIdx(ref n, currentBody.Position);
					stack[sp++] = (currentChildIdx, currentBody, currentDepth + 1, 0);
				}
			}

			pool.Return(stack);
		}

		private static int SelectChildIdx(ref Node n, Vector2D pos)
		{
			var midx = 0.5 * (n.L + n.R);
			var midy = 0.5 * (n.T + n.B);
			var east = pos.X >= midx;
			var south = pos.Y >= midy;

			return south
					   ? east
							 ? n.SE
							 : n.SW
					   : east
						   ? n.NE
						   : n.NW;
		}

		private void Subdivide(int idx)
		{
			var n = _nodes[idx];
			var midx = 0.5 * (n.L + n.R);
			var midy = 0.5 * (n.T + n.B);
			n.NW = NewNode(n.L, n.T, midx, midy);
			n.NE = NewNode(midx, n.T, n.R, midy);
			n.SW = NewNode(n.L, midy, midx, n.B);
			n.SE = NewNode(midx, midy, n.R, n.B);
			_nodes[idx] = n;
		}

		private void Accumulate(ref double mass, ref Vector2D wcom, int childIdx)
		{
			if(childIdx < 0)
				return;

			var c = _nodes[childIdx];
			mass += c.Mass;
			wcom += c.Mass * c.Com;
		}

		#endregion
	}
}