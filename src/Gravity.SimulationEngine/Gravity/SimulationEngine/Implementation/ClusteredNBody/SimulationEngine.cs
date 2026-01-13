using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Implementation.ClusteredNBody;

internal sealed class SimulationEngine : SimulationEngineBase
{
	#region Fields

	private readonly IIntegrator _integrator;

	#endregion

	#region Construction

	public SimulationEngine()
		: this(new LeapfrogIntegrator())
	{
	}

	public SimulationEngine(IIntegrator integrator)
		=> _integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnSimulate(IWorld world, Entity[] entities, TimeSpan deltaTime)
	{
		var collisions = _integrator.Integrate(entities, deltaTime, ApplyPhysics);

		if(collisions.Length != 0)
		{
			var entitiesById = entities.ToDictionary(e => e.Id);
			var seen = new HashSet<long>(collisions.Length * 2);

			for(var i = 0; i < collisions.Length; i++)
			{
				(var id1, var id2) = collisions[i];
				var a = Math.Min(id1, id2);
				var b = Math.Max(id1, id2);
				var key = ((long)a << 32) | (uint)b;

				if(!seen.Add(key))
					continue;

				var e1 = entitiesById[a];
				var e2 = entitiesById[b];

				(var v1, var v2) = HandleCollision(e1, e2, world.ElasticCollisions);

				if(v1.HasValue &&
				   v2.HasValue)
				{
					(var p1, var p2) = CancelOverlap(e1, e2);
					if(p1.HasValue)
						e1.Position = p1.Value;
					if(p2.HasValue)
						e2.Position = p2.Value;
				}

				if(v1.HasValue)
					e1.v = v1.Value;
				if(v2.HasValue)
					e2.v = v2.Value;
			}
		}
	}

	private static Tuple<int, int>[] ApplyPhysics(Entity[] entities)
	{
		var n = entities.Length;

		if(n == 0)
			return Array.Empty<Tuple<int, int>>();

		// Compute bounds
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < n; i++)
		{
			var p = entities[i].Position;
			if(p.X < l)
				l = p.X;
			if(p.Y < t)
				t = p.Y;
			if(p.X > r)
				r = p.X;
			if(p.Y > b)
				b = p.Y;
		}

		if(double.IsInfinity(l) ||
		   double.IsInfinity(t) ||
		   double.IsInfinity(r) ||
		   double.IsInfinity(b))
		{
			l = t = -1.0;
			r = b = 1.0;
		}

		// Choose grid resolution ~ target cluster size for forces
		const int targetClusterSize = 64;
		var gridSide = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, n / (double)targetClusterSize))));
		var spanX = Math.Max(1e-12, r - l);
		var spanY = Math.Max(1e-12, b - t);
		var qx = (gridSide - 1) / spanX;
		var qy = (gridSide - 1) / spanY;

		var codes = new uint[n];
		var idx = new int[n];
		var cellX = new int[n];
		var cellY = new int[n];

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed)
				continue;

			var p = entities[i].Position;
			var u = (int)Math.Round((p.X - l) * qx);
			var v = (int)Math.Round((p.Y - t) * qy);
			u = Math.Min(Math.Max(u, 0), gridSide - 1);
			v = Math.Min(Math.Max(v, 0), gridSide - 1);
			cellX[i] = u;
			cellY[i] = v;
			codes[i] = EncodeMorton2D((uint)u, (uint)v);
			idx[i] = i;
		}

		Array.Sort(codes, idx);

		// Build clusters (one per occupied cell)
		var clusterStarts = new List<int>(n / targetClusterSize + 1);
		var clusterLens = new List<int>(n / targetClusterSize + 1);
		var clusterU = new List<int>(n / targetClusterSize + 1);
		var clusterV = new List<int>(n / targetClusterSize + 1);
		var clusterMass = new List<double>(n / targetClusterSize + 1);
		var clusterCom = new List<Vector2D>(n / targetClusterSize + 1);

		var iStart = 0;

		while(iStart < n)
		{
			var iEnd = iStart + 1;
			var code = codes[iStart];
			while(iEnd < n &&
				  codes[iEnd] == code)
				iEnd++;
			var u = (int)DecodeMorton2DX(code);
			var v = (int)DecodeMorton2DY(code);
			var mSum = 0.0;
			var pSum = Vector2D.Zero;

			for(var k = iStart; k < iEnd; k++)
			{
				var e = entities[idx[k]];

				if(e.IsAbsorbed)
					continue;

				mSum += e.m;
				pSum += e.m * e.Position;
			}

			if(mSum > 0)
			{
				clusterStarts.Add(iStart);
				clusterLens.Add(iEnd - iStart);
				clusterU.Add(u);
				clusterV.Add(v);
				clusterMass.Add(mSum);
				clusterCom.Add(pSum / mSum);
			}

			iStart = iEnd;
		}

		var clusters = clusterStarts.Count;

		// Map cell(u,v) -> cluster index (-1 = empty)
		var cellToCluster = new int[gridSide][];

		for(var x = 0; x < gridSide; x++)
		{
			cellToCluster[x] = new int[gridSide];
			for(var y = 0; y < gridSide; y++)
				cellToCluster[x][y] = -1;
		}

		for(var c = 0; c < clusters; c++)
			cellToCluster[clusterU[c]][clusterV[c]] = c;

		// Compute accelerations (clustered forces)
		Parallel.For(0, n, i =>
						   {
							   var ei = entities[i];

							   if(ei.IsAbsorbed)
							   {
								   ei.a = Vector2D.Zero;

								   return;
							   }

							   var ai = Vector2D.Zero;
							   var ui = cellX[i];
							   var vi = cellY[i];

							   // Exact within 8-neighborhood cells
							   for(var dv = -1; dv <= 1; dv++)
							   {
								   var vy = vi + dv;

								   if(vy < 0 ||
									  vy >= gridSide)
									   continue;

								   for(var du = -1; du <= 1; du++)
								   {
									   var ux = ui + du;

									   if(ux < 0 ||
										  ux >= gridSide)
										   continue;

									   var cIdx = cellToCluster[ux][vy];

									   if(cIdx < 0)
										   continue;

									   var start = clusterStarts[cIdx];
									   var len = clusterLens[cIdx];

									   for(var k = start; k < start + len; k++)
									   {
										   var j = idx[k];

										   if(j == i)
											   continue;

										   var ej = entities[j];

										   if(ej.IsAbsorbed)
											   continue;

										   var d = ei.Position - ej.Position;
										   var d2 = d.LengthSquared + 1e-18;
										   var inv = 1.0 / Math.Pow(d2, 1.5);
										   ai += -IWorld.G * ej.m * d * inv;
									   }
								   }
							   }

							   // Aggregated for other clusters
							   for(var c = 0; c < clusters; c++)
							   {
								   var cu = clusterU[c];
								   var cv = clusterV[c];

								   if(Math.Abs(cu - ui) <= 1 &&
									  Math.Abs(cv - vi) <= 1)
									   continue;

								   var com = clusterCom[c];
								   var d = ei.Position - com;
								   var d2 = d.LengthSquared + 1e-18;
								   var inv = 1.0 / Math.Pow(d2, 1.5);
								   ai += -IWorld.G * clusterMass[c] * d * inv;
							   }

							   ei.a = ai;
						   });

		// High-accuracy collision detection pass (uniform grid, variable neighborhood)
		var collisions = new List<Tuple<int, int>>(Math.Min(n, 1024));
		var rMax = 0.0;
		for(var i = 0; i < n; i++)
			if(!entities[i].IsAbsorbed &&
			   entities[i].r > rMax)
				rMax = entities[i].r;
		var cellSize = Math.Max(1e-12, rMax);
		var cols = Math.Max(1, (int)Math.Ceiling(spanX / cellSize) + 1);
		var rows = Math.Max(1, (int)Math.Ceiling(spanY / cellSize) + 1);
		var buckets = new List<int>[cols * rows];

		static int Key(int x, int y, int cols)
			=> y * cols + x;

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed)
				continue;

			var p = entities[i].Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			var k = Key(cx, cy, cols);
			var bucket = buckets[k];
			if(bucket == null)
				buckets[k] = bucket = new(4);
			bucket.Add(i);
		}

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed)
				continue;

			var ei = entities[i];
			var p = ei.Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			var range = (int)Math.Ceiling(ei.r / cellSize);

			for(var dv = -range; dv <= range; dv++)
			{
				var yy = cy + dv;

				if(yy < 0 ||
				   yy >= rows)
					continue;

				for(var du = -range; du <= range; du++)
				{
					var xx = cx + du;

					if(xx < 0 ||
					   xx >= cols)
						continue;

					var bucket = buckets[Key(xx, yy, cols)];

					if(bucket == null)
						continue;

					for(var bi = 0; bi < bucket.Count; bi++)
					{
						var j = bucket[bi];

						if(j <= i)
							continue;

						var ej = entities[j];

						if(ej.IsAbsorbed)
							continue;

						var d = ei.Position - ej.Position;
						var d2 = d.LengthSquared;
						var sumR = ei.r + ej.r;
						var sumR2 = sumR * sumR;
						if(d2 <= sumR2)
							collisions.Add(Tuple.Create(ei.Id, ej.Id));
					}
				}
			}
		}

		return collisions.Count == 0
				   ? Array.Empty<Tuple<int, int>>()
				   : collisions.ToArray();
	}

	// Morton helpers (2D)
	private static uint EncodeMorton2D(uint x, uint y)
		=> (Part1By1(y) << 1) | Part1By1(x);

	private static uint DecodeMorton2DX(uint code)
		=> Compact1By1(code);

	private static uint DecodeMorton2DY(uint code)
		=> Compact1By1(code >> 1);

	private static uint Part1By1(uint x)
	{
		x &= 0x0000FFFF;
		x = (x | (x << 8)) & 0x00FF00FF;
		x = (x | (x << 4)) & 0x0F0F0F0F;
		x = (x | (x << 2)) & 0x33333333;
		x = (x | (x << 1)) & 0x55555555;

		return x;
	}

	private static uint Compact1By1(uint x)
	{
		x &= 0x55555555;
		x = (x ^ (x >> 1)) & 0x33333333;
		x = (x ^ (x >> 2)) & 0x0F0F0F0F;
		x = (x ^ (x >> 4)) & 0x00FF00FF;
		x = (x ^ (x >> 8)) & 0x0000FFFF;

		return x;
	}

	#endregion
}