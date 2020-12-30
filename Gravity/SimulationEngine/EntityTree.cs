using System;
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
			private int mEntities;
			private Entity mEntity;
			private Vector mTopLeft;
			private Vector mBottomRight;

			#endregion

			#region Construction

			public EntityNode(Vector aTopLeft, Vector aBottomRight)
			{
				mTopLeft = aTopLeft;
				mBottomRight = aBottomRight;
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

				switch (aChildNodeIndex)
				{
					case 0: return new EntityNode(mTopLeft, mTopLeft + size / 2);
					case 1: return new EntityNode(new Vector(mTopLeft.X + size.X / 2, mTopLeft.Y), new Vector(mBottomRight.X, mTopLeft.Y + size.Y / 2));
					case 2: return new EntityNode(new Vector(mTopLeft.X, mTopLeft.Y + size.Y / 2), new Vector(mBottomRight.X + size.X / 2, mBottomRight.Y));
					case 3: return new EntityNode(mTopLeft + size / 2, mBottomRight);
				}

				throw new ArgumentOutOfRangeException(nameof(aChildNodeIndex));
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

		private readonly EntityNode mRootNode;

		#endregion

		#region Construction

		public EntityTree(Vector aTopLeft, Vector aBottomRight)
			=> mRootNode = new EntityNode(aTopLeft, aBottomRight);

		#endregion

		#region Interface

		public void Add(Entity aEntity)
			=> mRootNode.Add(aEntity);

		#endregion
	}
}