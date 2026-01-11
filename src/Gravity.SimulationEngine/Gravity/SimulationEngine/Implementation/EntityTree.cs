// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class EntityTree
{
	#region Internal types

	internal record struct CollisionPair(Entity First, Entity Second);

	private sealed class EntityNode
	{
		#region Fields

		private Vector2D _bottomRight;
		private readonly EntityNode?[] _childNodes = new EntityNode?[4];
		private double _nodeSizeLenSq; // cached squared size
		private Vector2D _topLeft;
		private EntityTree _tree = null!; // set in Init
		private Vector2D _centerOfMass;
		private int _entities;
		private Entity? _entity;
		private double _mass;

		#endregion

		#region Construction

		public EntityNode(Vector2D topLeft, Vector2D bottomRight, EntityTree tree)
		{
			Init(topLeft, bottomRight, tree);
		}

		#endregion

		#region Interface

		public void Init(Vector2D topLeft, Vector2D bottomRight, EntityTree tree)
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
			for(var i = 0; i < _childNodes.Length; i++)
				_childNodes[i] = null;
		}

		public void Add(Entity entity)
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
		}

		[SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality", Justification = "<Pending>")]
		public Vector2D CalculateGravity(Entity entity)
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

					return -IWorld.G * _mass * dist * invR3;
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

		// Lock-free variant using an external collector
		public Vector2D CalculateGravity(Entity entity, List<CollisionPair> collector)
		{
			if(_entities == 1)
			{
				if(ReferenceEquals(_entity, entity))
					return Vector2D.Zero;

				var dist = entity.Position - _entity!.Position;
				var distLenSq = dist.LengthSquared;

				// Collision check using squared radii
				var sumR = entity.r + _entity.r;
				var sumR2 = sumR * sumR;
				if(distLenSq < sumR2)
				{
					collector.Add(new(_entity, entity));

					if(distLenSq <= double.Epsilon)
						return Vector2D.Zero;

					// project to surface along normalized direction without extra sqrt:
					// dist = dist * (sumR / |dist|)
					var invLen = 1.0d / Math.Sqrt(distLenSq);
					var scale = sumR * invLen;
					dist = dist * scale;
					distLenSq = sumR2;
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

				// Barnes–Hut acceptance: compare node size to distance; avoid extra sqrt
				if(_nodeSizeLenSq < _tree._thetaSquared * distLenSq)
				{
					var invRNode = 1.0d / Math.Sqrt(distLenSq);
					var invR3 = invRNode * invRNode * invRNode;

					return -IWorld.G * _mass * dist * invR3;
				}

				var ret = Vector2D.Zero;

				for(var i = 0; i < _childNodes.Length; i++)
				{
					var childNode = _childNodes[i];

					if(childNode == null)
						continue;

					ret += childNode.CalculateGravity(entity, collector);
				}

				return ret;
			}
		}

		public void ReleaseToPool()
		{
			for (var i = 0; i < _childNodes.Length; i++)
			{
				var child = _childNodes[i];
				if (child != null)
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
				if (_nodePool.Count < _maxPoolSize)
					_nodePool.Push(node);
			}
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
					   0     => _tree.RentNode(_topLeft, _topLeft + size / 2),
					   1     => _tree.RentNode(new(_topLeft.X + size.X / 2, _topLeft.Y), new(_bottomRight.X, _topLeft.Y + size.Y / 2)),
					   2     => _tree.RentNode(new(_topLeft.X, _topLeft.Y + size.Y / 2), new(_topLeft.X + size.X / 2, _bottomRight.Y)),
					   3     => _tree.RentNode(_topLeft + size / 2, _bottomRight),
					   var _ => throw new ArgumentOutOfRangeException(nameof(childNodeIndex))
				   };
		}

		private int GetChildNodeIndex(Vector2D position)
		{
			// Compute half extents once
			var halfWidth = (_bottomRight.X - _topLeft.X) * 0.5;
			var halfHeight = (_bottomRight.Y - _topLeft.Y) * 0.5;
			var dx = position.X - _topLeft.X;
			var dy = position.Y - _topLeft.Y;

			// Branchless-ish quadrant selection: left(0)/right(1), top(0)/bottom(1)
			var isRight = dx >= halfWidth ? 1 : 0;   // 0=left, 1=right
			var isBottom = dy >= halfHeight ? 1 : 0; // 0=top, 1=bottom

			// Map (isRight, isBottom) to child index: (0,0)->0, (1,0)->1, (0,1)->2, (1,1)->3
			return (isBottom << 1) | isRight;
		}

		#endregion

		// Iterative traversal to reduce recursion overhead
		public Vector2D CalculateGravityIterative(Entity entity)
		{
			var dummy = _tree.CollidedEntities;
			lock(dummy)
			{
				dummy.Clear();
				return CalculateGravityIterative(entity, dummy);
			}
		}

		public Vector2D CalculateGravityIterative(Entity entity, List<CollisionPair> collector)
		{
			var result = Vector2D.Zero;
			var pool = System.Buffers.ArrayPool<EntityNode>.Shared;
			var stack = pool.Rent(256);
			var sp = 0;
			stack[sp++] = this;

			while(sp > 0)
			{
				var node = stack[--sp];

				if(node._entities == 1)
				{
					if(ReferenceEquals(node._entity, entity))
						continue;

					var dist = entity.Position - node._entity!.Position;
					var distLenSq = dist.LengthSquared;
					var sumR = entity.r + node._entity.r;
					var sumR2 = sumR * sumR;
					if(distLenSq < sumR2)
					{
						collector.Add(new(node._entity, entity));
						if(distLenSq <= double.Epsilon)
							continue;
						var invLen = 1.0d / Math.Sqrt(distLenSq);
						var scale = sumR * invLen;
						dist = dist * scale;
						distLenSq = sumR2;
					}
					var invR = 1.0d / Math.Sqrt(distLenSq);
					var invR3 = invR * invR * invR;
					result += -IWorld.G * node._entity.m * dist * invR3;
				}
				else
				{
					var dist = entity.Position - node._centerOfMass;
					var distLenSq = dist.LengthSquared;
					if(node._nodeSizeLenSq < _tree._thetaSquared * distLenSq)
					{
						var invRNode = 1.0d / Math.Sqrt(distLenSq);
						var invR3 = invRNode * invRNode * invRNode;
						result += -IWorld.G * node._mass * dist * invR3;
						continue;
					}
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
				}
			}

			pool.Return(stack, clearArray:false);
			return result;
		}
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

	public EntityTree(Vector2D topLeft, Vector2D bottomRight, double theta)
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

	public void Add(Entity entity)
		=> _rootNode.Add(entity);

	public Vector2D CalculateGravity(Entity entity)
		=> _rootNode.CalculateGravityIterative(entity);

	public Vector2D CalculateGravity(Entity entity, List<CollisionPair> collector)
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