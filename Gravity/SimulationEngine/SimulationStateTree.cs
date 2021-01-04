using System;
using System.Linq;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class SimulationStateTree
	{
		#region Internal types

		private class Node
		{
			#region Fields

			private readonly Node[] mChildNodes = new Node[4];
			private readonly SimulationStateTree mTree;
			private int mStates;
			private SimulationState mState;
			private Vector mTopLeft;
			private Vector mBottomRight;
			private Vector mCenterOfMass;
			private double mMass;

			#endregion

			#region Construction

			public Node(Vector aTopLeft, Vector aBottomRight, SimulationStateTree aTree)
			{
				mTopLeft = aTopLeft;
				mBottomRight = aBottomRight;
				mTree = aTree;
			}

			#endregion

			#region Interface

			public void Add(SimulationState aState)
			{
				switch (mStates)
				{
					case 0:
					{
						mState = aState;
						break;
					}
					case 1:
					{
						var childNode = GetOrCreateChildNode(mState.Position);

						childNode.Add(mState);

						mState = null;

						childNode = GetOrCreateChildNode(aState.Position);

						childNode.Add(aState);
						break;
					}
					default:
					{
						var childNode = GetOrCreateChildNode(aState.Position);

						childNode.Add(aState);
						break;
					}
				}

				mStates++;
			}

			public void ComputeMassDistribution()
			{
				if (mStates == 1)
				{
					mCenterOfMass = mState.Position;
					mMass = mState.m;
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

			public Vector CalculateGravity(SimulationState aState)
			{
				if (mStates == 1)
				{
					if (ReferenceEquals(mState, aState))
						return VectorExtensions.Zero;

					var dist = aState.Position - mState.Position;

					//if (dist.Length < aState.r + mState.r)
					//{
					//	lock (mTree.CollidedEntities)
					//	{
					//		mTree.CollidedEntities.Add(Tuple.Create(mState, aState));
					//	}

					//	dist = dist.Unit() * (aState.r + mState.r);
					//}

					return -World.G * mState.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
				}
				else
				{
					var dist = aState.Position - mCenterOfMass;
					var nodeSize = mBottomRight - mTopLeft;

					if (nodeSize.Length / dist.Length < mTree.mTheta)
						return -World.G * mMass * dist / Math.Pow(dist.LengthSquared, 1.5d);

					var ret = VectorExtensions.Zero;

					foreach (var childNode in mChildNodes.Where(n => null != n))
						ret += childNode.CalculateGravity(aState);

					return ret;
				}
			}

			#endregion

			#region Implementation

			private Node GetOrCreateChildNode(Vector aPosition)
			{
				var childNodeIndex = GetChildNodeIndex(aPosition);

				return mChildNodes[childNodeIndex]
					   ?? (mChildNodes[childNodeIndex] = CreateChildNode(childNodeIndex));
			}

			private Node CreateChildNode(int aChildNodeIndex)
			{
				var size = mBottomRight - mTopLeft;

				return aChildNodeIndex switch
				{
					0 => new Node(mTopLeft, mTopLeft + size / 2, mTree),
					1 => new Node(new Vector(mTopLeft.X + size.X / 2, mTopLeft.Y), new Vector(mBottomRight.X, mTopLeft.Y + size.Y / 2), mTree),
					2 => new Node(new Vector(mTopLeft.X, mTopLeft.Y + size.Y / 2), new Vector(mTopLeft.X + size.X / 2, mBottomRight.Y), mTree),
					3 => new Node(mTopLeft + size / 2, mBottomRight, mTree),
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
		private readonly Node mRootNode;

		#endregion

		#region Construction

		public SimulationStateTree(Vector aTopLeft, Vector aBottomRight, double aTheta)
		{
			mTheta = aTheta;
			mRootNode = new Node(aTopLeft, aBottomRight, this);
		}

		#endregion

		#region Interface

		//public List<Tuple<Entity, Entity>> CollidedEntities { get; } = new List<Tuple<Entity, Entity>>();

		public void ComputeMassDistribution()
			=> mRootNode.ComputeMassDistribution();

		public void Add(SimulationState aState)
			=> mRootNode.Add(aState);

		public Vector CalculateGravity(SimulationState aState)
			=> mRootNode.CalculateGravity(aState);

		#endregion
	}
}