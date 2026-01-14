using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

// Highly optimized Barnes–Hut style quadtree focused for AdaptiveSimulationEngine
// - Allocation-light (pooled array-backed nodes)
// - No collision collection (acceleration-only)
// - Iterative traversal for CalculateGravity
internal sealed class BarnesHutTree
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

	public BarnesHutTree(in Vector2D topLeft, in Vector2D bottomRight, double theta, int estimatedBodies)
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

	public BarnesHutTree(in Vector2D topLeft, in Vector2D bottomRight, double theta)
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

	public void Add(Body e)
		=> Insert(_root, e, 0);

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
		var pool = ArrayPool<int>.Shared;
		var stack = pool.Rent(256);
		var sp = 0;
		stack[sp++] = _root;
		var thetaSq = _theta * _theta;
		long localVisits = 0;

		while(sp > 0)
		{
			var idx = stack[--sp];
			localVisits++;
			var n = _nodes[idx];

			if(n.Mass <= 0.0)
				continue;

			var dvec = e.Position - n.Com;
			var dist2 = dvec.LengthSquared;

			if(dist2 <= 0.0)
				continue;

			var leaf = n is { NW: < 0, Body: not null } or { NW: < 0, AggMass: > 0.0 };

			if(leaf || n.WidthSq / dist2 < thetaSq)
			{
				var invLen3 = 1.0 / (dist2 * Math.Sqrt(dist2));
				acc += -IWorld.G * n.Mass * invLen3 * dvec;
			}
			else
			{
				if(sp + 4 >= stack.Length)
				{
					var bigger = pool.Rent(stack.Length * 2);
					Array.Copy(stack, bigger, sp);
					pool.Return(stack);
					stack = bigger;
				}

				if(n.NW >= 0)
					stack[sp++] = n.NW;
				if(n.NE >= 0)
					stack[sp++] = n.NE;
				if(n.SW >= 0)
					stack[sp++] = n.SW;
				if(n.SE >= 0)
					stack[sp++] = n.SE;
			}
		}

		pool.Return(stack);
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
		if(depth > MaxDepthReached)
			MaxDepthReached = depth;
		var n = _nodes[idx];
		n.Count++;
		var hasChildren = n.NW >= 0;

		if(!hasChildren &&
		   n.Body == null &&
		   n.AggMass > 0.0)
		{
			n.AggMass += e.m;
			n.AggWeightedCom += e.m * e.Position;
			_nodes[idx] = n;

			return;
		}

		if(!hasChildren &&
		   n.Body == null &&
		   n.Count == 1)
		{
			n.Body = e;
			_nodes[idx] = n;

			return;
		}

		if(!hasChildren)
		{
			var widthEdge = Math.Max(EpsilonSize, n.R - n.L);
			var heightEdge = Math.Max(EpsilonSize, n.B - n.T);

			if(depth >= MaxDepth ||
			   (widthEdge <= EpsilonSize && heightEdge <= EpsilonSize))
			{
				if(n.Body != null)
				{
					n.AggMass += n.Body.m;
					n.AggWeightedCom += n.Body.m * n.Body.Position;
					n.Body = null;
				}

				n.AggMass += e.m;
				n.AggWeightedCom += e.m * e.Position;
				_nodes[idx] = n;

				return;
			}

			Subdivide(idx);
			n = _nodes[idx];

			if(n.Body != null)
			{
				var prev = n.Body;
				n.Body = null;
				n.Count--;
				_nodes[idx] = n;
				Insert(SelectChild(idx, prev.Position), prev, depth + 1);
				n = _nodes[idx];
			}
		}

		_nodes[idx] = n;
		Insert(SelectChild(idx, e.Position), e, depth + 1);
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

	private int SelectChild(int idx, in Vector2D p)
	{
		var n = _nodes[idx];
		var midx = 0.5 * (n.L + n.R);
		var midy = 0.5 * (n.T + n.B);
		var east = p.X >= midx;
		var south = p.Y >= midy;

		if(south)
			return east
					   ? n.SE
					   : n.SW;

		return east
				   ? n.NE
				   : n.NW;
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