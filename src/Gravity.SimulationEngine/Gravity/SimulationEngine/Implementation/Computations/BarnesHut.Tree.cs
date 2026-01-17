using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Gravity.SimulationEngine.Implementation.Computations;

internal sealed partial class BarnesHut
{
	// Highly optimized Barnes–Hut style octree (3D)
	// - Allocation-light (pooled array-backed nodes)
	// - No collision collection (acceleration-only)
	// - Iterative traversal for CalculateGravity
	public sealed class Tree
	{
		#region Internal types

		private struct Node
		{
			#region Fields

			// For aggregated leaf (depth limit), accumulate weighted COM while inserting
			public double AggMass;
			public Vector3D AggWeightedCom;

			// Leaf payload
			public Body? Body;
			public Vector3D Com;
			public int Count; // number of bodies aggregated in this node

			// 3D Bounds (Left, Top, Front to Right, Bottom, Back)
			public double MinX, MinY, MinZ;
			public double MaxX, MaxY, MaxZ;

			// Mass and center of mass
			public double Mass;

			// 8 Children indices for octree (-1 if none)
			// Named by octant: [X < mid][Y < mid][Z < mid]
			public int Child0; // X<, Y<, Z< (front-top-left)
			public int Child1; // X>=, Y<, Z< (front-top-right)
			public int Child2; // X<, Y>=, Z< (front-bottom-left)
			public int Child3; // X>=, Y>=, Z< (front-bottom-right)
			public int Child4; // X<, Y<, Z>= (back-top-left)
			public int Child5; // X>=, Y<, Z>= (back-top-right)
			public int Child6; // X<, Y>=, Z>= (back-bottom-left)
			public int Child7; // X>=, Y>=, Z>= (back-bottom-right)

			// Cached squared width for traversal criterion
			public double WidthSq;

			#endregion

			#region Interface

			public readonly bool HasChildren => Child0 >= 0;

			public readonly int GetChild(int index) => index switch
			{
				0 => Child0,
				1 => Child1,
				2 => Child2,
				3 => Child3,
				4 => Child4,
				5 => Child5,
				6 => Child6,
				7 => Child7,
				_ => -1
			};

			#endregion
		}

		#endregion

		#region Fields

		private const double EpsilonSize = 1e-12; // minimal node size to avoid endless subdivision
		private const int MaxDepth = 32; // safety bound for degenerate cases

		// Thread-local stack pool to eliminate contention during parallel CalculateGravity calls
		[ThreadStatic] private static int[]? _threadLocalStack;

		private readonly double _minX, _minY, _minZ;
		private readonly double _maxX, _maxY, _maxZ;

		private readonly int _root;
		private readonly double _theta;
		private Node[] _nodes;
		private long _traversalVisitCount;

		#endregion

		#region Construction

		public Tree(in Vector3D topLeft, in Vector3D bottomRight, double theta, int estimatedBodies)
		{
			_theta = theta;
			_minX = topLeft.X;
			_minY = topLeft.Y;
			_minZ = topLeft.Z;
			_maxX = bottomRight.X;
			_maxY = bottomRight.Y;
			_maxZ = bottomRight.Z;
			var initialCapacity = Math.Max(1024, Math.Min(estimatedBodies * 8, 2_000_000));
			_nodes = ArrayPool<Node>.Shared.Rent(initialCapacity);
			NodeCount = 0;
			_root = NewNode(_minX, _minY, _minZ, _maxX, _maxY, _maxZ);
		}

		public Tree(in Vector3D topLeft, in Vector3D bottomRight, double theta)
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
			var width = Math.Max(EpsilonSize, _maxX - _minX);
			var height = Math.Max(EpsilonSize, _maxY - _minY);
			var depth = Math.Max(EpsilonSize, _maxZ - _minZ);
			var invW = 1.0 / width;
			var invH = 1.0 / height;
			var invD = 1.0 / depth;
			var n = entities.Length;
			var pool = ArrayPool<(ulong key, Body e)>.Shared;
			var arr = pool.Rent(n);

			// Compute 3D Morton code for spatial sorting
			for(var i = 0; i < n; i++)
			{
				var p = entities[i].Position;
				var nx = Math.Clamp((p.X - _minX) * invW, 0.0, 1.0);
				var ny = Math.Clamp((p.Y - _minY) * invH, 0.0, 1.0);
				var nz = Math.Clamp((p.Z - _minZ) * invD, 0.0, 1.0);
				var x = (uint)(nx * (1u << 21)); // 21 bits per dimension for 63-bit Morton code
				var y = (uint)(ny * (1u << 21));
				var z = (uint)(nz * (1u << 21));
				arr[i] = (InterleaveBits3D(x, y, z), entities[i]);
			}

			Array.Sort(arr, 0, n, Comparer<(ulong key, Body e)>.Create((a, b) => a.key.CompareTo(b.key)));
			for(var i = 0; i < n; i++)
				Insert(_root, arr[i].e, 0);
			pool.Return(arr);
		}

		public void ComputeMassDistribution()
		{
			var pool = ArrayPool<(int idx, int state)>.Shared;
			var stack = pool.Rent(256);
			var sp = 0;
			stack[sp++] = (_root, 0);

			while(sp > 0)
			{
				(var idx, var state) = stack[--sp];
				var n = _nodes[idx];

				if(state == 0 && n.HasChildren)
				{
					if(sp + 9 >= stack.Length)
					{
						var bigger = pool.Rent(stack.Length * 2);
						Array.Copy(stack, bigger, sp);
						pool.Return(stack);
						stack = bigger;
					}

					stack[sp++] = (idx, 1);
					for(var c = 0; c < 8; c++)
					{
						var child = n.GetChild(c);
						if(child >= 0)
							stack[sp++] = (child, 0);
					}
				}
				else
				{
					// Cache width^2 once per node for traversal (use max dimension)
					var wx = Math.Max(EpsilonSize, n.MaxX - n.MinX);
					var wy = Math.Max(EpsilonSize, n.MaxY - n.MinY);
					var wz = Math.Max(EpsilonSize, n.MaxZ - n.MinZ);
					var w = Math.Max(wx, Math.Max(wy, wz));
					n.WidthSq = w * w;

					if(n.HasChildren)
					{
						var mass = 0.0;
						var wcom = Vector3D.Zero;
						for(var c = 0; c < 8; c++)
							Accumulate(ref mass, ref wcom, n.GetChild(c));
						n.Mass = mass;
						n.Com = mass > 0.0
									? wcom / mass
									: Vector3D.Zero;
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

		public Vector3D CalculateGravity(Body e)
		{
			var acc = Vector3D.Zero;

			// Use thread-local stack to eliminate ArrayPool contention across threads
#pragma warning disable S2696 // Instance method intentionally sets ThreadStatic field
			var stack = _threadLocalStack;

			if(stack == null)
			{
				stack = new int[512];
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
				var dz = ePos.Z - com.Z;
				var dist2 = dx * dx + dy * dy + dz * dz;

				if(dist2 <= 0.0)
					continue;

				var isLeaf = !n.HasChildren && (n.Body != null || n.AggMass > 0.0);

				if(isLeaf || n.WidthSq / dist2 < thetaSq)
				{
					// Fused calculation: avoid separate sqrt and divisions
					var dist = Math.Sqrt(dist2);
					var invLen3 = 1.0 / (dist2 * dist);
					var factor = -IWorld.G * mass * invLen3;
					acc = acc + new Vector3D(factor * dx, factor * dy, factor * dz);
				}
				else
				{
					// Expand children - handle stack growth if needed
					if(sp + 8 >= stack.Length)
					{
#pragma warning disable S2696
						// Grow thread-local stack (rare case for very deep trees)
						var newStack = new int[stack.Length * 2];
						Array.Copy(stack, newStack, sp);
						_threadLocalStack = stack = newStack;
#pragma warning restore S2696
					}

					// Push all existing children
					for(var c = 0; c < 8; c++)
					{
						var child = n.GetChild(c);
						if(child >= 0)
							stack[sp++] = child;
					}
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

		/// <summary>
		/// Interleave 3 sets of 21 bits into a 63-bit Morton code
		/// </summary>
		private static ulong InterleaveBits3D(uint x, uint y, uint z)
		{
			return SplitBy3(x) | (SplitBy3(y) << 1) | (SplitBy3(z) << 2);
		}

		/// <summary>
		/// Spread 21 bits so there are 2 zero bits between each original bit
		/// </summary>
		private static ulong SplitBy3(uint v)
		{
			ulong x = v & 0x1FFFFF; // 21 bits
			x = (x | (x << 32)) & 0x1F00000000FFFF;
			x = (x | (x << 16)) & 0x1F0000FF0000FF;
			x = (x | (x << 8)) & 0x100F00F00F00F00F;
			x = (x | (x << 4)) & 0x10C30C30C30C30C3;
			x = (x | (x << 2)) & 0x1249249249249249;
			return x;
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

		private int NewNode(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
		{
			var idx = NodeCount;
			EnsureCapacity(idx + 1);
			var n = new Node
					{
						MinX = minX,
						MinY = minY,
						MinZ = minZ,
						MaxX = maxX,
						MaxY = maxY,
						MaxZ = maxZ,
						WidthSq = 0.0,
						Child0 = -1,
						Child1 = -1,
						Child2 = -1,
						Child3 = -1,
						Child4 = -1,
						Child5 = -1,
						Child6 = -1,
						Child7 = -1,
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
			var stack = pool.Rent(128);
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

					if(!n.HasChildren &&
					   n.Body == null &&
					   n.AggMass > 0.0)
					{
						n.AggMass += currentBody.m;
						n.AggWeightedCom += currentBody.m * currentBody.Position;

						continue;
					}

					if(!n.HasChildren &&
					   n.Body == null &&
					   n.Count == 1)
					{
						n.Body = currentBody;

						continue;
					}

					if(!n.HasChildren)
					{
						var widthX = Math.Max(EpsilonSize, n.MaxX - n.MinX);
						var widthY = Math.Max(EpsilonSize, n.MaxY - n.MinY);
						var widthZ = Math.Max(EpsilonSize, n.MaxZ - n.MinZ);

						if(currentDepth >= MaxDepth ||
						   (widthX <= EpsilonSize && widthY <= EpsilonSize && widthZ <= EpsilonSize))
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

		private static int SelectChildIdx(ref Node n, Vector3D pos)
		{
			var midX = 0.5 * (n.MinX + n.MaxX);
			var midY = 0.5 * (n.MinY + n.MaxY);
			var midZ = 0.5 * (n.MinZ + n.MaxZ);
			
			// Compute octant index: bit 0 = X>=mid, bit 1 = Y>=mid, bit 2 = Z>=mid
			var octant = (pos.X >= midX ? 1 : 0) |
						 (pos.Y >= midY ? 2 : 0) |
						 (pos.Z >= midZ ? 4 : 0);

			return octant switch
			{
				0 => n.Child0,
				1 => n.Child1,
				2 => n.Child2,
				3 => n.Child3,
				4 => n.Child4,
				5 => n.Child5,
				6 => n.Child6,
				7 => n.Child7,
				_ => -1
			};
		}

		private void Subdivide(int idx)
		{
			var n = _nodes[idx];
			var midX = 0.5 * (n.MinX + n.MaxX);
			var midY = 0.5 * (n.MinY + n.MaxY);
			var midZ = 0.5 * (n.MinZ + n.MaxZ);

			// Create 8 octant children
			n.Child0 = NewNode(n.MinX, n.MinY, n.MinZ, midX, midY, midZ);     // X<, Y<, Z<
			n.Child1 = NewNode(midX, n.MinY, n.MinZ, n.MaxX, midY, midZ);     // X>=, Y<, Z<
			n.Child2 = NewNode(n.MinX, midY, n.MinZ, midX, n.MaxY, midZ);     // X<, Y>=, Z<
			n.Child3 = NewNode(midX, midY, n.MinZ, n.MaxX, n.MaxY, midZ);     // X>=, Y>=, Z<
			n.Child4 = NewNode(n.MinX, n.MinY, midZ, midX, midY, n.MaxZ);     // X<, Y<, Z>=
			n.Child5 = NewNode(midX, n.MinY, midZ, n.MaxX, midY, n.MaxZ);     // X>=, Y<, Z>=
			n.Child6 = NewNode(n.MinX, midY, midZ, midX, n.MaxY, n.MaxZ);     // X<, Y>=, Z>=
			n.Child7 = NewNode(midX, midY, midZ, n.MaxX, n.MaxY, n.MaxZ);     // X>=, Y>=, Z>=

			_nodes[idx] = n;
		}

		private void Accumulate(ref double mass, ref Vector3D wcom, int childIdx)
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
