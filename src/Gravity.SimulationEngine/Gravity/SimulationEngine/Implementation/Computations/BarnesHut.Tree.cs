using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Gravity.SimulationEngine.Implementation.Computations;

internal sealed partial class BarnesHut
{
	#region Internal types

	// Highly optimized Barnes–Hut style octree (3D)
	// - Allocation-light (pooled array-backed nodes)
	// - No collision collection (acceleration-only)
	// - Iterative traversal for CalculateGravity
	public sealed class Tree
	{
		#region Internal types

		/// <summary>
		/// Inline array of 8 child indices for octree nodes.
		/// Using InlineArray for O(1) indexed access without switch overhead.
		/// </summary>
		[InlineArray(8)]
		private struct ChildrenArray
		{
			#region Fields

#pragma warning disable S1144 // Required by InlineArray
			private int _element0;
#pragma warning restore S1144

			#endregion
		}

		private struct Node
		{
			#region Fields

			// For aggregated leaf (depth limit), accumulate weighted COM while inserting
			public double _aggMass;
			public Vector3D _aggWeightedCom;

			// Leaf payload
			public Body? _body;

			// 8 Children indices for octree (-1 if none)
			// Using InlineArray for O(1) indexed access
#pragma warning disable S3459 // Assigned via indexer in NewNode
			public ChildrenArray _children;
#pragma warning restore S3459
			public Vector3D _com;
			public int _count; // number of bodies aggregated in this node

			// Mass and center of mass
			public double _mass;

			public double _maxX,
						  _maxY,
						  _maxZ;

			// 3D Bounds (Left, Top, Front to Right, Bottom, Back)
			public double _minX,
						  _minY,
						  _minZ;

			// Cached squared width for traversal criterion
			public double _widthSq;

			#endregion

			#region Interface

			public readonly bool HasChildren
				=> _children[0] >= 0;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public readonly int GetChild(int index)
				=> _children[index];

			#endregion
		}

		#endregion

		#region Fields

		private const double _epsilonSize = 1e-12; // minimal node size to avoid endless subdivision
		private const int _maxDepth = 32; // safety bound for degenerate cases

		// Thread-local stack pool to eliminate contention during parallel CalculateGravity calls
		[ThreadStatic] private static int[]? _threadLocalStack;

		private readonly double _maxX,
								_maxY,
								_maxZ;

		private readonly double _minX,
								_minY,
								_minZ;

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

		public void AddRange(IReadOnlyList<Body> entities)
		{
			var width = Math.Max(_epsilonSize, _maxX - _minX);
			var height = Math.Max(_epsilonSize, _maxY - _minY);
			var depth = Math.Max(_epsilonSize, _maxZ - _minZ);
			var invW = 1.0 / width;
			var invH = 1.0 / height;
			var invD = 1.0 / depth;
			var n = entities.Count;
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

				if(state == 0 &&
				   n.HasChildren)
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
					var wx = Math.Max(_epsilonSize, n._maxX - n._minX);
					var wy = Math.Max(_epsilonSize, n._maxY - n._minY);
					var wz = Math.Max(_epsilonSize, n._maxZ - n._minZ);
					var w = Math.Max(wx, Math.Max(wy, wz));
					n._widthSq = w * w;

					if(n.HasChildren)
					{
						var mass = 0.0;
						var wcom = Vector3D.Zero;
						for(var c = 0; c < 8; c++)
							Accumulate(ref mass, ref wcom, n.GetChild(c));
						n._mass = mass;
						n._com = mass > 0.0
									 ? wcom / mass
									 : Vector3D.Zero;
						_nodes[idx] = n;
					}
					else
					{
						if(n._body != null)
						{
							n._mass = n._body.m;
							n._com = n._body.Position;
							_nodes[idx] = n;
						}
						else if(n._aggMass > 0.0)
						{
							n._mass = n._aggMass;
							n._com = n._aggWeightedCom / n._aggMass;
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

				var mass = n._mass;

				if(mass <= 0.0)
					continue;

				// Cache node center of mass
				var com = n._com;
				var dx = ePos.X - com.X;
				var dy = ePos.Y - com.Y;
				var dz = ePos.Z - com.Z;
				var dist2 = dx * dx + dy * dy + dz * dz;

				if(dist2 <= 0.0)
					continue;

				var isLeaf = !n.HasChildren && (n._body != null || n._aggMass > 0.0);

				if(isLeaf || n._widthSq / dist2 < thetaSq)
				{
					// For leaf nodes with a body, use sum of radii for clamping
					// For aggregated nodes, use a conservative estimate based on node width
					double minDistSq;
					if(isLeaf && n._body != null)
					{
						var sumR = e.r + n._body.r;
						minDistSq = sumR * sumR;
					}
					else
					{
						// For aggregated masses, use body radius * 4 as conservative minimum
						minDistSq = 16.0 * e.r * e.r;
					}
					
					var effectiveDist2 = dist2 < minDistSq ? minDistSq : dist2;
					var effectiveDist = Math.Sqrt(effectiveDist2);
					var invLen3 = 1.0 / (effectiveDist2 * effectiveDist);
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
			=> SplitBy3(x) | (SplitBy3(y) << 1) | (SplitBy3(z) << 2);

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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int SelectChildIdx(ref Node n, Vector3D pos)
		{
			var midX = 0.5 * (n._minX + n._maxX);
			var midY = 0.5 * (n._minY + n._maxY);
			var midZ = 0.5 * (n._minZ + n._maxZ);

			// Compute octant index: bit 0 = X>=mid, bit 1 = Y>=mid, bit 2 = Z>=mid
			var octant = (pos.X >= midX
							  ? 1
							  : 0) |
						 (pos.Y >= midY
							  ? 2
							  : 0) |
						 (pos.Z >= midZ
							  ? 4
							  : 0);

			return n._children[octant];
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
						_minX = minX,
						_minY = minY,
						_minZ = minZ,
						_maxX = maxX,
						_maxY = maxY,
						_maxZ = maxZ,
						_widthSq = 0.0,
						_body = null,
						_count = 0,
						_mass = 0.0,
						_com = default,
						_aggMass = 0.0,
						_aggWeightedCom = default
					};
			// Initialize all children to -1
			for(var i = 0; i < 8; i++)
				n._children[i] = -1;
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
					n._count++;

					if(!n.HasChildren &&
					   n._body == null &&
					   n._aggMass > 0.0)
					{
						n._aggMass += currentBody.m;
						n._aggWeightedCom += currentBody.m * currentBody.Position;

						continue;
					}

					if(!n.HasChildren &&
					   n._body == null &&
					   n._count == 1)
					{
						n._body = currentBody;

						continue;
					}

					if(!n.HasChildren)
					{
						var widthX = Math.Max(_epsilonSize, n._maxX - n._minX);
						var widthY = Math.Max(_epsilonSize, n._maxY - n._minY);
						var widthZ = Math.Max(_epsilonSize, n._maxZ - n._minZ);

						if(currentDepth >= _maxDepth ||
						   (widthX <= _epsilonSize && widthY <= _epsilonSize && widthZ <= _epsilonSize))
						{
							if(n._body != null)
							{
								n._aggMass += n._body.m;
								n._aggWeightedCom += n._body.m * n._body.Position;
								n._body = null;
							}

							n._aggMass += currentBody.m;
							n._aggWeightedCom += currentBody.m * currentBody.Position;

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

						if(n._body != null)
						{
							var prev = n._body;
							n._body = null;
							n._count--;

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

		private void Subdivide(int idx)
		{
			ref var n = ref _nodes[idx];
			var midX = 0.5 * (n._minX + n._maxX);
			var midY = 0.5 * (n._minY + n._maxY);
			var midZ = 0.5 * (n._minZ + n._maxZ);

			// Create 8 octant children
			n._children[0] = NewNode(n._minX, n._minY, n._minZ, midX, midY, midZ); // X<, Y<, Z<
			n._children[1] = NewNode(midX, n._minY, n._minZ, n._maxX, midY, midZ); // X>=, Y<, Z<
			n._children[2] = NewNode(n._minX, midY, n._minZ, midX, n._maxY, midZ); // X<, Y>=, Z<
			n._children[3] = NewNode(midX, midY, n._minZ, n._maxX, n._maxY, midZ); // X>=, Y>=, Z<
			n._children[4] = NewNode(n._minX, n._minY, midZ, midX, midY, n._maxZ); // X<, Y<, Z>=
			n._children[5] = NewNode(midX, n._minY, midZ, n._maxX, midY, n._maxZ); // X>=, Y<, Z>=
			n._children[6] = NewNode(n._minX, midY, midZ, midX, n._maxY, n._maxZ); // X<, Y>=, Z>=
			n._children[7] = NewNode(midX, midY, midZ, n._maxX, n._maxY, n._maxZ); // X>=, Y>=, Z>=
		}

		private void Accumulate(ref double mass, ref Vector3D wcom, int childIdx)
		{
			if(childIdx < 0)
				return;

			var c = _nodes[childIdx];
			mass += c._mass;
			wcom += c._mass * c._com;
		}

		#endregion
	}

	#endregion
}