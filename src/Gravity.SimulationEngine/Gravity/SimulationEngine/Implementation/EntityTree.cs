// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class EntityTree
{
	#region Internal types

	private sealed class EntityNode
	{
		#region Fields

		private readonly Vector2D _bottomRight;
		private readonly EntityNode[] _childNodes = new EntityNode[4];
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
						_tree.CollidedEntities.Add(Tuple.Create(entity, _entity));

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

			foreach(var childNode in _childNodes.Where(n => null != n))
			{
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

				if(dist.Length < entity.r + _entity.r)
				{
					lock(_tree.CollidedEntities)
						_tree.CollidedEntities.Add(Tuple.Create(_entity, entity));

					if(dist.LengthSquared == 0.0d)
						return Vector2D.Zero;

					dist = dist.Unit() * (entity.r + _entity.r);
				}

				return -IWorld.G * _entity.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
			}
			else
			{
				var dist = entity.Position - _centerOfMass;
				var nodeSize = _bottomRight - _topLeft;

				if(nodeSize.Length / dist.Length < _tree._theta)
					return -IWorld.G * _mass * dist / Math.Pow(dist.LengthSquared, 1.5d);

				var ret = Vector2D.Zero;

				foreach(var childNode in _childNodes.Where(n => null != n))
					ret += childNode.CalculateGravity(entity);

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
			var size = _bottomRight - _topLeft;
			var pos = position - _topLeft;

			return pos.X < size.X / 2
					   ? pos.Y < size.Y / 2
							 ? 0
							 : 2
					   : pos.Y < size.Y / 2
						   ? 1
						   : 3;
		}

		#endregion
	}

	#endregion

	#region Fields

	private readonly EntityNode _rootNode;
	private readonly double _theta;

	#endregion

	#region Construction

	public EntityTree(Vector2D topLeft, Vector2D bottomRight, double theta)
	{
		_theta = theta;
		_rootNode = new(topLeft, bottomRight, this);
	}

	#endregion

	#region Interface

	public List<Tuple<Entity, Entity>> CollidedEntities { get; } = new();

	public void ComputeMassDistribution()
		=> _rootNode.ComputeMassDistribution();

	public void Add(Entity entity)
		=> _rootNode.Add(entity);

	public Vector2D CalculateGravity(Entity entity)
		=> _rootNode.CalculateGravity(entity);

	#endregion
}