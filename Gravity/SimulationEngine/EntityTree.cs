using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class EntityTree
	{
		#region Internal types

		private class EntityNode
		{
			#region Fields

			private readonly EntityNode[] mChildNodes = new EntityNode[4];
			private readonly EntityTree mTree;
			private int mEntities;
			private Entity mEntity;
			private Vector mTopLeft;
			private Vector mBottomRight;
			private Vector mCenterOfMass;
			private double mMass;

			#endregion

			#region Construction

			public EntityNode(Vector aTopLeft, Vector aBottomRight, EntityTree aTree)
			{
				mTopLeft = aTopLeft;
				mBottomRight = aBottomRight;
				mTree = aTree;
			}

			#endregion

			#region Interface

			public void Add(Entity aEntity)
			{
				switch (mEntities)
				{
					case 0:
					{
						mEntity = aEntity;
						break;
					}
					case 1:
					{
						if ((aEntity.Position - mEntity.Position).Length <= (aEntity.r + mEntity.r))
						{
							mTree.CollidedEntities.Add(Tuple.Create(aEntity, mEntity));
							return;
						}

						var childNode = GetOrCreateChildNode(mEntity.Position);

						childNode.Add(mEntity);

						mEntity = null;

						childNode = GetOrCreateChildNode(aEntity.Position);

						childNode.Add(aEntity);
						break;
					}
					default:
					{
						var childNode = GetOrCreateChildNode(aEntity.Position);

						childNode.Add(aEntity);
						break;
					}
				}

				mEntities++;
			}

			public void ComputeMassDistribution()
			{
				if (mEntities == 1)
				{
					mCenterOfMass = mEntity.Position;
					mMass = mEntity.m;
					return;
				}

				mCenterOfMass = VectorExtensions.Zero;
				mMass = 0;

				foreach (var childNode in mChildNodes.Where(n => null != n))
				{
					childNode.ComputeMassDistribution();

					mMass += childNode.mMass;
					mCenterOfMass += childNode.mMass * childNode.mCenterOfMass;
				}

				mCenterOfMass /= mMass;
			}

			public Vector CalculateGravity(Entity aEntity)
			{
				if (mEntities == 1)
				{
					if (ReferenceEquals(mEntity, aEntity))
						return VectorExtensions.Zero;

					var dist = aEntity.Position - mEntity.Position;

					if (dist.Length < aEntity.r + mEntity.r)
					{
						lock (mTree.CollidedEntities) 
							mTree.CollidedEntities.Add(Tuple.Create(mEntity, aEntity));

						if(dist.LengthSquared==0.0d)
							return VectorExtensions.Zero;

						dist = dist.Unit() * (aEntity.r + mEntity.r);
					}

					return -World.G * mEntity.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
				}
				else
				{
					var dist = aEntity.Position - mCenterOfMass;
					var nodeSize = mBottomRight - mTopLeft;

					if (nodeSize.Length / dist.Length < mTree.mTheta)
						return -World.G * mMass * dist / Math.Pow(dist.LengthSquared, 1.5d);

					var ret = VectorExtensions.Zero;

					foreach (var childNode in mChildNodes.Where(n => null != n))
						ret += childNode.CalculateGravity(aEntity);

					return ret;
				}
			}

			#endregion

			#region Implementation

			private EntityNode GetOrCreateChildNode(Vector aPosition)
			{
				var childNodeIndex = GetChildNodeIndex(aPosition);

				return mChildNodes[childNodeIndex]
					   ?? (mChildNodes[childNodeIndex] = CreatEntityNode(childNodeIndex));
			}

			private EntityNode CreatEntityNode(int aChildNodeIndex)
			{
				var size = mBottomRight - mTopLeft;

				return aChildNodeIndex switch
				{
					0 => new EntityNode(mTopLeft, mTopLeft + size / 2, mTree),
					1 => new EntityNode(new Vector(mTopLeft.X + size.X / 2, mTopLeft.Y), new Vector(mBottomRight.X, mTopLeft.Y + size.Y / 2), mTree),
					2 => new EntityNode(new Vector(mTopLeft.X, mTopLeft.Y + size.Y / 2), new Vector(mTopLeft.X + size.X / 2, mBottomRight.Y), mTree),
					3 => new EntityNode(mTopLeft + size / 2, mBottomRight, mTree),
					_ => throw new ArgumentOutOfRangeException(nameof(aChildNodeIndex))
				};
			}

			private int GetChildNodeIndex(Vector aPosition)
			{
				var size = mBottomRight - mTopLeft;
				var position = aPosition - mTopLeft;

				return (position.X < size.X / 2)
						   ? (position.Y < size.Y / 2)
								 ? 0
								 : 2
						   : (position.Y < size.Y / 2)
							   ? 1
							   : 3;
			}

			#endregion
		}

		#endregion

		#region Fields

		private readonly double mTheta;
		private readonly EntityNode mRootNode;

		#endregion

		#region Construction

		public EntityTree(Vector aTopLeft, Vector aBottomRight, double aTheta)
		{
			mTheta = aTheta;
			mRootNode = new EntityNode(aTopLeft, aBottomRight, this);
		}

		#endregion

		#region Interface

		public List<Tuple<Entity, Entity>> CollidedEntities { get; } = new List<Tuple<Entity, Entity>>();

		public void ComputeMassDistribution()
			=> mRootNode.ComputeMassDistribution();

		public void Add(Entity aEntity)
			=> mRootNode.Add(aEntity);

		public Vector CalculateGravity(Entity aEntity)
			=> mRootNode.CalculateGravity(aEntity);

		#endregion
	}
}