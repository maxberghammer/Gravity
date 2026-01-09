// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation;

public sealed class EntityTree
{
	#region Internal types

	private sealed class EntityNode
	{
		#region Fields

		private readonly Vector2D _bottomRight;
		private readonly EntityNode[] _childNodes = new EntityNode[4];
		private readonly Vector2D _topLeft;
		private readonly EntityTree _tree;
		private readonly double _nodeSizeLengthSquared; // cached squared length
		private Vector2D _centerOfMass;
		private int _entities;
		private Entity? _entity;
		private double _mass;
		private double _massG;

		#endregion

		#region Construction

		public EntityNode(Vector2D topLeft, Vector2D bottomRight, EntityTree tree)
		{
			_topLeft = topLeft;
			_bottomRight = bottomRight;
			_tree = tree;
			var size = _bottomRight - _topLeft;
			var len = size.Length;
			_nodeSizeLengthSquared = len * len;
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
						_tree.AddCollision(entity, _entity);
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
				_massG = IWorld.G * _mass;
				return;
			}

			_centerOfMass = Vector2D.Zero;
			_mass = 0;
			_massG = 0;

			for (int i = 0; i < _childNodes.Length; i++)
			{
				var childNode = _childNodes[i];
				if (childNode == null) continue;
				childNode.ComputeMassDistribution();
				_mass += childNode._mass;
				_centerOfMass += childNode._mass * childNode._centerOfMass;
			}

			_centerOfMass /= _mass;
			_massG = IWorld.G * _mass;
		}

        [SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality", Justification = "<Pending>")]
        public Vector2D CalculateGravity(Entity entity)
        {
            const double Epsilon = 1e-12;
            if (_entities == 1)
            {
                if (ReferenceEquals(_entity, entity))
                    return Vector2D.Zero;

                // In-place component math to reduce temporary Vector2D structs
                var dx = entity.Position.X - _entity!.Position.X;
                var dy = entity.Position.Y - _entity!.Position.Y;
                var r2 = dx * dx + dy * dy;

                var minDist = entity.r + _entity.r;
                var minDist2 = minDist * minDist;

                if (r2 < minDist2)
                {
                    _tree.AddCollision(_entity, entity);

                    if (r2 < Epsilon)
                        return Vector2D.Zero;

                    var invLen = 1.0d / Math.Sqrt(r2);
                    var scale = minDist * invLen;
                    dx *= scale;
                    dy *= scale;
                    r2 = minDist2;
                }

                // invR3 = 1 / (r^3) => 1 / (r2 * sqrt(r2)); avoids extra division
                var invR3 = 1.0d / (r2 * Math.Sqrt(r2));
                var coeff = -_massG * invR3; // uses cached G*m
                return new Vector2D(coeff * dx, coeff * dy);
            }
            else
            {
                // In-place component math to reduce temporary Vector2D structs
                var dx = entity.Position.X - _centerOfMass.X;
                var dy = entity.Position.Y - _centerOfMass.Y;
                var r2 = dx * dx + dy * dy;
                if (r2 < Epsilon)
                    return Vector2D.Zero;

                // Barnes-Hut opening criterion using squared values without division: s2 < theta^2 * r2
                var s2 = _nodeSizeLengthSquared;
                if (s2 < _tree.ThetaSquared * r2)
                {
                    // invR3 = 1 / (r^3) => 1 / (r2 * sqrt(r2)); avoids extra division
                    var invR3 = 1.0d / (r2 * Math.Sqrt(r2));
                    var coeff = -_massG * invR3; // uses cached G*m
                    return new Vector2D(coeff * dx, coeff * dy);
                }

                var ret = Vector2D.Zero;
                for (int i = 0; i < _childNodes.Length; i++)
                {
                    var childNode = _childNodes[i];
                    if (childNode == null) continue;
                    ret += childNode.CalculateGravity(entity);
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
	private readonly double _thetaSquared;
	private readonly ConcurrentBag<Tuple<Entity, Entity>> _collisions = new();

	#endregion

	#region Construction

	public EntityTree(Vector2D topLeft, Vector2D bottomRight, double theta)
	{
		_theta = theta;
		_thetaSquared = theta * theta;
		_rootNode = new(topLeft, bottomRight, this);
	}

	#endregion

	#region Interface

	public IReadOnlyList<Tuple<Entity, Entity>> CollidedEntities => _collisions.ToArray();

	internal void AddCollision(Entity a, Entity b)
	{
		_collisions.Add(Tuple.Create(a, b));
	}

	public void ComputeMassDistribution()
		=> _rootNode.ComputeMassDistribution();

	public void Add(Entity entity)
	{
		if (entity is null) throw new ArgumentNullException(nameof(entity));
		_rootNode.Add(entity);
	}

	public Vector2D CalculateGravity(Entity entity)
	{
		if (entity is null) throw new ArgumentNullException(nameof(entity));
		return _rootNode.CalculateGravity(entity);
	}

	public double Theta => _theta;
    public double ThetaSquared => _thetaSquared;

    #endregion
}