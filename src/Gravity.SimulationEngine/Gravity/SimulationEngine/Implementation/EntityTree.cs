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

		private readonly Vector2D _bottomRight;
		private readonly EntityNode[] _childNodes = new EntityNode[4];
		private readonly double _nodeSizeLenSq; // cached squared size
		private readonly Vector2D _topLeft;
		private readonly EntityTree _tree;
		private Vector2D _centerOfMass;
		private int _entities;
		private Entity? _entity;
		private double _mass;

		#endregion

		#region Construction

		public EntityNode(Vector2D topLeft, Vector2D bottomRight, EntityTree tree)
		{
			_topLeft = topLeft;
			_bottomRight = bottomRight;
			_tree = tree;
			var size = _bottomRight - _topLeft;
			_nodeSizeLenSq = size.LengthSquared;
		}

		#endregion

		#region Interface

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
				if(distLenSq < sumR * sumR)
				{
					collector.Add(new(_entity, entity));

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

					ret += childNode.CalculateGravity(entity, collector);
				}

				return ret;
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
					   0     => new(_topLeft, _topLeft + size / 2, _tree),
					   1     => new(new(_topLeft.X + size.X / 2, _topLeft.Y), new(_bottomRight.X, _topLeft.Y + size.Y / 2), _tree),
					   2     => new(new(_topLeft.X, _topLeft.Y + size.Y / 2), new(_topLeft.X + size.X / 2, _bottomRight.Y), _tree),
					   3     => new(_topLeft + size / 2, _bottomRight, _tree),
					   var _ => throw new ArgumentOutOfRangeException(nameof(childNodeIndex))
				   };
		}

		private int GetChildNodeIndex(Vector2D position)
		{
			var halfWidth = (_bottomRight.X - _topLeft.X) / 2.0;
			var halfHeight = (_bottomRight.Y - _topLeft.Y) / 2.0;
			var dx = position.X - _topLeft.X;
			var dy = position.Y - _topLeft.Y;

			if(dx < halfWidth)
				return dy < halfHeight
						   ? 0
						   : 2;

			return dy < halfHeight
					   ? 1
					   : 3;
		}

		#endregion
	}

	#endregion

	#region Fields

	private readonly EntityNode _rootNode;
	private readonly double _thetaSquared;

	#endregion

	#region Construction

	public EntityTree(Vector2D topLeft, Vector2D bottomRight, double theta)
	{
		_thetaSquared = theta * theta;
		_rootNode = new(topLeft, bottomRight, this);
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
		=> _rootNode.CalculateGravity(entity);

	public Vector2D CalculateGravity(Entity entity, List<CollisionPair> collector)
		=> _rootNode.CalculateGravity(entity, collector);

	#endregion
}