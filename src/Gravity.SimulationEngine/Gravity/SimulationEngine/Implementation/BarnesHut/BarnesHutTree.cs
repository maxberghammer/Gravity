// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine.Implementation.BarnesHut;

internal sealed class BarnesHutTree
{
	#region Internal types

	internal record struct CollisionPair(Body First, Body Second);

	private sealed class EntityNode
	{
		#region Fields

		private Vector2D _bottomRight;
		private readonly EntityNode?[] _childNodes = new EntityNode?[4];
		private double _nodeSizeLenSq; // cached squared size
		private Vector2D _topLeft;
		private BarnesHutTree _tree = null!; // set in Init
		private Vector2D _centerOfMass;
		private int _entities;
		private Body? _entity;
		private double _mass;
		private double _gm; // cached -G * mass for aggregated nodes

		#endregion

		#region Construction

		public EntityNode(Vector2D topLeft, Vector2D bottomRight, BarnesHutTree tree)
		{
			Init(topLeft, bottomRight, tree);
		}

		#endregion

		#region Interface

		public void Init(Vector2D topLeft, Vector2D bottomRight, BarnesHutTree tree)
		{
			_topLeft = topLeft;
			_bottomRight = bottomRight;
			_tree = tree;
			var size = _bottomRight - _topLeft;
			_nodeSizeLenSq = size.LengthSquared;
			_centerOfMass = Vector2D.Zero;
			_entities = 0;
			_entity = null;
			_mass = 0;
			_gm = 0;
			for(var i = 0; i < _childNodes.Length; i++)
				_childNodes[i] = null;
		}

		public void Add(Body entity)
		{
			switch(_entities)
			{
				case 0:
				{
					_entity = entity;

					break;
				}
				case 1:
				{
					if((entity.Position - _entity!.Position).Length <= entity.r + _entity.r)
					{
						_tree.CollidedEntities.Add(new(entity, _entity));

						return;
					}

					var childNode = GetOrCreateChildNode(_entity.Position);

					childNode.Add(_entity);

					_entity = null;

					childNode = GetOrCreateChildNode(entity.Position);

					childNode.Add(entity);

					break;
				}
				default:
				{
					var childNode = GetOrCreateChildNode(entity.Position);

					childNode.Add(entity);

					break;
				}
			}

			_entities++;
		}

		public void ComputeMassDistribution()
		{
			if(_entities == 1)
			{
				_centerOfMass = _entity!.Position;
				_mass = _entity.m;
				_gm = -IWorld.G * _mass;

				return;
			}

			_centerOfMass = Vector2D.Zero;
			_mass = 0;

			for(var i = 0; i < _childNodes.Length; i++)
			{
				var childNode = _childNodes[i];

				if(childNode == null)
					continue;

				childNode.ComputeMassDistribution();

				_mass += childNode._mass;
				_centerOfMass += childNode._mass * childNode._centerOfMass;
			}

			_centerOfMass /= _mass;
			_gm = -IWorld.G * _mass;
		}

		[SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality", Justification = "<Pending>")]
		public Vector2D CalculateGravity(Body entity)
		{
			if(_entities == 1)
			{
				if(ReferenceEquals(_entity, entity))
					return Vector2D.Zero;

				var dist = entity.Position - _entity!.Position;
				var distLenSq = dist.LengthSquared;

				// Collision check using squared radii
				var sumR = entity.r + _entity.r;
				if(distLenSq < sumR * sumR)
				{
					lock(_tree.CollidedEntities)
						_tree.CollidedEntities.Add(new(_entity, entity));

					if(distLenSq <= double.Epsilon)
						return Vector2D.Zero;

					// project to surface along normalized direction without extra sqrt
					var invRCollision = 1.0d / Math.Sqrt(distLenSq);
					dist = dist * (sumR * invRCollision);
					distLenSq = sumR * sumR;
				}

				// invR3 = 1 / r^3 using a single sqrt
				var invR = 1.0d / Math.Sqrt(distLenSq);
				var invR3 = invR * invR * invR;

				return -IWorld.G * _entity.m * dist * invR3;
			}
			else
			{
				var dist = entity.Position - _centerOfMass;
				var distLenSq = dist.LengthSquared;

				if(_nodeSizeLenSq < _tree._thetaSquared * distLenSq)
				{
					var invRNode = 1.0d / Math.Sqrt(distLenSq);
					var invR3 = invRNode * invRNode * invRNode;

					return _gm * dist * invR3; // use cached -G*M
				}

				var ret = Vector2D.Zero;

				for(var i = 0; i < _childNodes.Length; i++)
				{
					var childNode = _childNodes[i];

					if(childNode == null)
						continue;

					ret += childNode.CalculateGravity(entity);
				}

				return ret;
			}
		}

		// Iterative traversal to reduce recursion overhead
		public Vector2D CalculateGravityIterative(Body entity)
		{
			var dummy = _tree.CollidedEntities;
			lock(dummy)
			{
				dummy.Clear();
				return CalculateGravityIterative(entity, dummy);
			}
		}

		[SuppressMessage("Major Bug", "S2681", Justification = "<Pending>")]
		public Vector2D CalculateGravityIterative(Body entity, List<CollisionPair> collector)
		{
			var result = Vector2D.Zero;
			var pool = System.Buffers.ArrayPool<EntityNode>.Shared;
			var stack = pool.Rent(256);
			var sp = 0;
			stack[sp++] = this;

			while(sp > 0)
			{
				var node = stack[--sp];

				// Fast-path: aggregated node accepted by Barnes–Hut
				if(node._entities != 1)
				{
					var dist = entity.Position - node._centerOfMass;
					var distLenSq = dist.LengthSquared;
					if(node._nodeSizeLenSq < _tree._thetaSquared * distLenSq)
					{
						var invR = 1.0d / Math.Sqrt(distLenSq);
						var invR2 = invR * invR;
						var invR3 = invR * invR2;
						result += node._gm * invR3 * dist;
						continue;
					}

					// Push non-null children
					for(var i = 0; i < node._childNodes.Length; i++)
					{
						var child = node._childNodes[i];
						if(child == null)
							continue;
						if(sp >= stack.Length)
						{
							var newStack = pool.Rent(stack.Length * 2);
							System.Array.Copy(stack, newStack, stack.Length);
							pool.Return(stack, clearArray:false);
							stack = newStack;
						}
						stack[sp++] = child;
					}
					continue;
				}

				// Leaf path: single entity
				if(ReferenceEquals(node._entity, entity))
					continue;

				var distLeaf = entity.Position - node._entity!.Position;
				var distLeafLenSq = distLeaf.LengthSquared;

				// Fused collision check using cached squared radii
				var ra = entity.r;
				var rb = node._entity.r;
				var sumR = ra + rb;
				var sumR2 = entity.r2 + node._entity.r2 + 2.0d * ra * rb;

				double invRLeaf;
				if(distLeafLenSq < sumR2)
				{
					collector.Add(new(node._entity, entity));
					if(distLeafLenSq <= double.Epsilon)
						continue;
					var invLen = 1.0d / Math.Sqrt(distLeafLenSq);
					distLeaf = distLeaf * (sumR * invLen);
					invRLeaf = 1.0d / sumR; // |dist| == sumR now
				}
				else
				{
					invRLeaf = 1.0d / Math.Sqrt(distLeafLenSq);
				}

				var invRLeaf2 = invRLeaf * invRLeaf;
				var invRLeaf3 = invRLeaf * invRLeaf2;
				var gmLeaf = -IWorld.G * node._entity.m;
				result += gmLeaf * invRLeaf3 * distLeaf;
			}

			pool.Return(stack, clearArray:false);
			return result;
		}
		#endregion

		#region Implementation

		private EntityNode GetOrCreateChildNode(Vector2D position)
		{
			var childNodeIndex = GetChildNodeIndex(position);
			return _childNodes[childNodeIndex]
				   ?? (_childNodes[childNodeIndex] = CreatEntityNode(childNodeIndex));
		}

		private EntityNode CreatEntityNode(int childNodeIndex)
		{
			var size = _bottomRight - _topLeft;
			return childNodeIndex switch
			{
				0 => _tree.RentNode(_topLeft, _topLeft + size / 2),
				1 => _tree.RentNode(new(_topLeft.X + size.X / 2, _topLeft.Y), new(_bottomRight.X, _topLeft.Y + size.Y / 2)),
				2 => _tree.RentNode(new(_topLeft.X, _topLeft.Y + size.Y / 2), new(_topLeft.X + size.X / 2, _bottomRight.Y)),
				3 => _tree.RentNode(_topLeft + size / 2, _bottomRight),
				_ => throw new ArgumentOutOfRangeException(nameof(childNodeIndex))
			};
		}

		private int GetChildNodeIndex(Vector2D position)
		{
			var halfWidth = (_bottomRight.X - _topLeft.X) * 0.5;
			var halfHeight = (_bottomRight.Y - _topLeft.Y) * 0.5;
			var dx = position.X - _topLeft.X;
			var dy = position.Y - _topLeft.Y;
			var isRight = dx >= halfWidth ? 1 : 0;
			var isBottom = dy >= halfHeight ? 1 : 0;
			return (isBottom << 1) | isRight;
		}

		public void ReleaseToPool()
		{
			for(var i = 0; i < _childNodes.Length; i++)
			{
				var child = _childNodes[i];
				if(child != null)
				{
					child.ReleaseToPool();
					_childNodes[i] = null;
				}
			}
			ReturnNode(this);
		}

		private static void ReturnNode(EntityNode node)
		{
			lock(_poolLock)
			{
				if(_nodePool.Count < _maxPoolSize)
					_nodePool.Push(node);
			}
		}

		#endregion
	}

	#endregion

	#region Fields

	private static readonly object _poolLock = new();
	private const int _maxPoolSize = 1 << 20; // cap pool to avoid unbounded growth
	private static readonly Stack<EntityNode> _nodePool = new();
	private readonly EntityNode _rootNode;
	private readonly double _thetaSquared;

	#endregion

	#region Construction

	public BarnesHutTree(Vector2D topLeft, Vector2D bottomRight, double theta)
	{
		_thetaSquared = theta * theta;
		_rootNode = RentNode(topLeft, bottomRight);
	}

	#endregion

	#region Interface

	public List<CollisionPair> CollidedEntities { get; } = new(16);

	public void ResetCollisions()
		=> CollidedEntities.Clear();

	public void ComputeMassDistribution()
		=> _rootNode.ComputeMassDistribution();

	public void Add(Body entity)
		=> _rootNode.Add(entity);

	public Vector2D CalculateGravity(Body entity)
		=> _rootNode.CalculateGravityIterative(entity);

	public Vector2D CalculateGravity(Body entity, List<CollisionPair> collector)
		=> _rootNode.CalculateGravityIterative(entity, collector);

	public void Release()
	{
		_rootNode.ReleaseToPool();
		CollidedEntities.Clear();
	}

	#endregion

	#region Implementation

	private EntityNode RentNode(Vector2D topLeft, Vector2D bottomRight)
	{
		EntityNode? node = null;
		lock(_poolLock)
		{
			if(_nodePool.Count > 0)
				node = _nodePool.Pop();
		}

		if(node != null)
		{
			node.Init(topLeft, bottomRight, this);

			return node;
		}

		return new EntityNode(topLeft, bottomRight, this);
	}

	#endregion
}