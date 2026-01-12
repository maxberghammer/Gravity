using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

// Highly optimized Barnes–Hut style quadtree focused for AdaptiveSimulationEngine
// - Allocation-light (index-based nodes)
// - No collision collection (acceleration-only)
// - Iterative traversal for CalculateGravity
internal sealed class BarnesHutTree
{
	private const double EpsilonSize = 1e-12;   // minimal node size to avoid endless subdivision
	private const int MaxDepth = 32;            // safety bound for degenerate cases

	private readonly double _theta;
	private readonly List<Node> _nodes;
	private readonly int _root;

	private struct Node
	{
		// Bounds
		public double L, T, R, B;
		// Mass and center of mass
		public double Mass;
		public Vector2D Com;
		// Children indices (-1 if none)
		public int NW, NE, SW, SE;
		// Leaf payload
		public Entity? Body;
		public int Count; // number of bodies aggregated in this node
		// For aggregated leaf (depth limit), accumulate weighted COM while inserting
		public double AggMass;
		public Vector2D AggWeightedCom;
	}

	public BarnesHutTree(in Vector2D topLeft, in Vector2D bottomRight, double theta, int estimatedBodies)
	{
		_theta = theta;
		var initialCapacity = Math.Max(1024, Math.Min(estimatedBodies * 4, 1_000_000));
		_nodes = new List<Node>(initialCapacity);
		_root = NewNode(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
	}

	public BarnesHutTree(in Vector2D topLeft, in Vector2D bottomRight, double theta)
		: this(topLeft, bottomRight, theta, 256) { }

	private int NewNode(double l, double t, double r, double b)
	{
		var n = new Node
		{
			L = l, T = t, R = r, B = b,
			NW = -1, NE = -1, SW = -1, SE = -1,
			Body = null, Count = 0, Mass = 0.0, Com = default,
			AggMass = 0.0, AggWeightedCom = default
		};
		_nodes.Add(n);
		return _nodes.Count - 1;
	}

	public void Add(Entity e)
		=> Insert(_root, e, 0);

	private void Insert(int idx, Entity e, int depth)
	{
		var n = _nodes[idx];
		n.Count++;
		bool hasChildren = n.NW >= 0;

		if(!hasChildren && n.Body == null && n.AggMass > 0.0)
		{
			n.AggMass += e.m;
			n.AggWeightedCom += e.m * e.Position;
			_nodes[idx] = n;
			return;
		}

		if(!hasChildren && n.Body == null && n.Count == 1)
		{
			n.Body = e;
			_nodes[idx] = n;
			return;
		}

		if(!hasChildren)
		{
			var width = Math.Max(EpsilonSize, n.R - n.L);
			var height = Math.Max(EpsilonSize, n.B - n.T);
			if(depth >= MaxDepth || (width <= EpsilonSize && height <= EpsilonSize))
			{
				// aggregate into this leaf
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

			// subdivide
			Subdivide(idx);
			// reload node after subdivision
			n = _nodes[idx];
			if(n.Body != null)
			{
				var prev = n.Body; n.Body = null; n.Count--; _nodes[idx] = n;
				Insert(SelectChild(idx, prev.Position), prev, depth + 1);
				// reload again because n may be outdated
				n = _nodes[idx];
			}
		}

		// save any pending changes before recursion
		_nodes[idx] = n;
		Insert(SelectChild(idx, e.Position), e, depth + 1);
	}

	private void Subdivide(int idx)
	{
		var n = _nodes[idx];
		double midx = 0.5 * (n.L + n.R);
		double midy = 0.5 * (n.T + n.B);
		n.NW = NewNode(n.L, n.T, midx, midy);
		n.NE = NewNode(midx, n.T, n.R, midy);
		n.SW = NewNode(n.L, midy, midx, n.B);
		n.SE = NewNode(midx, midy, n.R, n.B);
		_nodes[idx] = n;
	}

	private int SelectChild(int idx, in Vector2D p)
	{
		var n = _nodes[idx];
		double midx = 0.5 * (n.L + n.R);
		double midy = 0.5 * (n.T + n.B);
		bool east = p.X >= midx;
		bool south = p.Y >= midy;
		if(south)
			return east ? n.SE : n.SW;
		else
			return east ? n.NE : n.NW;
	}

	public void ComputeMassDistribution()
	{
		var stack = new Stack<(int idx, int state)>(64);
		stack.Push((_root, 0));
		while(stack.Count > 0)
		{
			var (idx, state) = stack.Pop();
			var n = _nodes[idx];
			bool hasChildren = n.NW >= 0;
			if(state == 0 && hasChildren)
			{
				// push compute step then children
				stack.Push((idx, 1));
				if(n.NW >= 0) stack.Push((n.NW, 0));
				if(n.NE >= 0) stack.Push((n.NE, 0));
				if(n.SW >= 0) stack.Push((n.SW, 0));
				if(n.SE >= 0) stack.Push((n.SE, 0));
			}
			else
			{
				if(hasChildren)
				{
					double mass = 0.0; Vector2D wcom = Vector2D.Zero;
					Accumulate(ref mass, ref wcom, n.NW);
					Accumulate(ref mass, ref wcom, n.NE);
					Accumulate(ref mass, ref wcom, n.SW);
					Accumulate(ref mass, ref wcom, n.SE);
					n.Mass = mass;
					n.Com = mass > 0.0 ? (wcom / mass) : Vector2D.Zero;
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
	}

	private void Accumulate(ref double mass, ref Vector2D wcom, int childIdx)
	{
		if(childIdx < 0) return;
		var c = _nodes[childIdx];
		mass += c.Mass;
		wcom += c.Mass * c.Com;
	}

	public Vector2D CalculateGravity(Entity e)
	{
		Vector2D acc = Vector2D.Zero;
		// Use an array-based stack to avoid per-call allocations from Stack<int>
		int[] stack = new int[64];
		int sp = 0;
		stack[sp++] = _root;
		double thetaSq = _theta * _theta;
		while(sp > 0)
		{
			int idx = stack[--sp];
			var n = _nodes[idx];
			if(n.Mass <= 0.0) continue;
			var dvec = e.Position - n.Com;
			double dist2 = dvec.LengthSquared;
			if(dist2 <= 0.0) continue;
			double width = Math.Max(EpsilonSize, n.R - n.L);
			double widthSq = width * width;
			bool leaf = (n.NW < 0 && n.Body != null) || (n.NW < 0 && n.AggMass > 0.0);
			if(leaf || (widthSq / dist2) < thetaSq)
			{
				double invLen3 = 1.0 / (dist2 * Math.Sqrt(dist2));
				acc += -IWorld.G * n.Mass * invLen3 * dvec;
			}
			else
			{
				if(n.NW >= 0) stack[sp++] = n.NW;
				if(n.NE >= 0) stack[sp++] = n.NE;
				if(n.SW >= 0) stack[sp++] = n.SW;
				if(n.SE >= 0) stack[sp++] = n.SE;
				// Grow stack if needed
				if(sp + 4 >= stack.Length)
				{
					var newStack = new int[stack.Length * 2];
					Array.Copy(stack, newStack, stack.Length);
					stack = newStack;
				}
			}
		}
		return acc;
	}

	public void Release() => _nodes.Clear();
}
