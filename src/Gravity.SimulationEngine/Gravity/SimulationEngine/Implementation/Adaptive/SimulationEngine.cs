using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

internal sealed class SimulationEngine : ISimulationEngine
{
	#region Tunables

	private const double Safety = 0.65;           // Sicherheitsfaktor für Oversampling-Schrittweite
	private const int MaxSubsteps = 64;           // Obergrenze pro Frame
	private const double MinDtSeconds = 1e-6;     // Untergrenze für numerische Stabilität

	#endregion

	#region Implementation of ISimulationEngine

	void ISimulationEngine.Simulate(Entity[] entities, TimeSpan deltaTime)
	{
		var n = entities.Length;
		if(n == 0 || deltaTime <= TimeSpan.Zero)
			return;

		var remaining = deltaTime.TotalSeconds;
		var steps = 0;

		while(remaining > 0.0 && steps < MaxSubsteps)
		{
			// 1) Kräfte bzw. Beschleunigungen berechnen (Barnes–Hut via EntityTree2)
			ComputeAccelerations(entities);

			// 2) dtO bestimmen: kleinstes (Durchmesser / Geschwindigkeit) über alle Entities
			double dtO = double.PositiveInfinity;
			for(var i = 0; i < n; i++)
			{
				var e = entities[i];
				if(e.IsAbsorbed) continue;
				var vlen = e.v.Length;
				if(vlen <= 0.0 || e.r <= 0.0) continue;
				var candidate = (2.0 * e.r) / vlen; // Zeit, um eigenen Durchmesser zu überqueren
				if(candidate < dtO) dtO = candidate;
			}
			if(double.IsInfinity(dtO) || dtO <= 0.0) dtO = remaining; // Keine sinnvolle Grenze -> ganzen Rest nehmen
			dtO = Math.Max(MinDtSeconds, Math.Min(remaining, Safety * dtO));

			// 3) v += a * dtO (semi-implizit stabil)
			Parallel.For(0, n, i =>
			{
				var e = entities[i];
				if(e.IsAbsorbed) return;
				e.v += e.a * dtO;
			});

			// 4) p += v * dtO
			Parallel.For(0, n, i =>
			{
				var e = entities[i];
				if(e.IsAbsorbed) return;
				e.Position += e.v * dtO;
			});

			// 5) Kollisionen nach dem Substep exakt erkennen und auflösen
			ResolveCollisions(entities);

			remaining -= dtO;
			steps++;
		}

		// Weltgrenzen behandeln (einmal am Ende genügt bei kleinen Substeps)
		var vp = entities.Length > 0 ? entities[0].World.Viewport : null;
		if(vp != null)
		{
			var tl = vp.TopLeft;
			var br = vp.BottomRight;
			for(var i = 0; i < entities.Length; i++)
				if(entities[i].World.ClosedBoundaries)
					entities[i].HandleCollisionWithWorldBoundaries(in tl, in br);
		}
		else
		{
			for(var i = 0; i < entities.Length; i++)
				if(entities[i].World.ClosedBoundaries)
					entities[i].HandleCollisionWithWorldBoundaries();
		}
	}

	#endregion

	#region Implementation

	private static void ComputeAccelerations(Entity[] entities)
	{
		int n = entities.Length;
		if(n == 0) return;

		// Bounds bestimmen
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < n; i++)
		{
			var p = entities[i].Position;
			if(p.X < l) l = p.X;
			if(p.Y < t) t = p.Y;
			if(p.X > r) r = p.X;
			if(p.Y > b) b = p.Y;
		}
		if(double.IsInfinity(l) || double.IsInfinity(t) || double.IsInfinity(r) || double.IsInfinity(b))
		{
			l = t = -1.0; r = b = 1.0;
		}

		// Theta adaptiv wie in Barnes–Hut (inkl. Small-N-Overrides)
		var theta = ComputeTheta(entities, l, t, r, b);

		var tree = new BarnesHutTree(new(l, t), new(r, b), theta);
		for(var i = 0; i < n; i++) tree.Add(entities[i]);
		tree.ComputeMassDistribution();

		Parallel.For(0, n, i =>
		{
			entities[i].a = tree.CalculateGravity(entities[i]);
		});

		tree.Release();
	}

	private static void ResolveCollisions(Entity[] entities)
	{
		var n = entities.Length;
		if(n <= 1) return;

		// Bounds
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity,
			   rMax = 0.0;
		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed) continue;
			var p = entities[i].Position;
			if(p.X < l) l = p.X;
			if(p.Y < t) t = p.Y;
			if(p.X > r) r = p.X;
			if(p.Y > b) b = p.Y;
			if(entities[i].r > rMax) rMax = entities[i].r;
		}
		if(double.IsInfinity(l) || double.IsInfinity(t) || double.IsInfinity(r) || double.IsInfinity(b))
			return;

		var spanX = Math.Max(1e-12, r - l);
		var spanY = Math.Max(1e-12, b - t);
		var cellSize = Math.Max(1e-9, rMax);
		var cols = Math.Max(1, (int)Math.Ceiling(spanX / cellSize) + 1);
		var rows = Math.Max(1, (int)Math.Ceiling(spanY / cellSize) + 1);
		var buckets = new List<int>[cols * rows];
		static int Key(int x, int y, int cols) => y * cols + x;

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed) continue;
			var p = entities[i].Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			var k = Key(cx, cy, cols);
			var bucket = buckets[k];
			if(bucket == null) buckets[k] = bucket = new List<int>(4);
			bucket.Add(i);
		}

		var elastic = entities[0].World.ElasticCollisions;
		var seen = new HashSet<long>(256);

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed) continue;
			var ei = entities[i];
			var p = ei.Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			var range = (int)Math.Ceiling(ei.r / cellSize) + 1;
			for(var dv = -range; dv <= range; dv++)
			{
				var yy = cy + dv; if(yy < 0 || yy >= rows) continue;
				for(var du = -range; du <= range; du++)
				{
					var xx = cx + du; if(xx < 0 || xx >= cols) continue;
					var bucket = buckets[Key(xx, yy, cols)];
					if(bucket == null) continue;
					for(var bi = 0; bi < bucket.Count; bi++)
					{
						var j = bucket[bi]; if(j <= i) continue;
						var ej = entities[j]; if(ej.IsAbsorbed) continue;
						var a = Math.Min(ei.Id, ej.Id);
						var bId = Math.Max(ei.Id, ej.Id);
						var key = ((long)a << 32) | (uint)bId;
						if(!seen.Add(key)) continue;
						var d = ei.Position - ej.Position;
						var sumR = ei.r + ej.r;
						if(d.LengthSquared <= sumR * sumR)
						{
							var (v1, v2) = ei.HandleCollision(ej, elastic);
							if(v1.HasValue && v2.HasValue)
							{
								var (p1, p2) = ei.CancelOverlap(ej);
								if(p1.HasValue) ei.Position = p1.Value;
								if(p2.HasValue) ej.Position = p2.Value;
							}
							if(v1.HasValue) ei.v = v1.Value;
							if(v2.HasValue) ej.v = v2.Value;
						}
					}
				}
			}
		}
	}

	private static double ComputeTheta(Entity[] entities, double l, double t, double r, double b)
	{
		var n = entities.Length;
		var width = Math.Max(1e-12, r - l);
		var height = Math.Max(1e-12, b - t);
		var span = Math.Max(width, height);
		double minSep = double.PositiveInfinity;
		for(int i = 0; i < Math.Min(n, 32); i++)
		{
			for(int j = i + 1; j < Math.Min(n, i + 32); j++)
			{
				var d = (entities[j].Position - entities[i].Position).Length;
				if(d < minSep) minSep = d;
			}
		}
		if(double.IsInfinity(minSep) || minSep <= 0) minSep = span;
		var sepRatio = Math.Clamp(minSep / span, 0.0, 1.0);

		if(n <= 3) return 0.0;
		if(n <= 10) return 0.1;
		if(n <= 50) return 0.2;

		double baseTheta = 0.62;
		double k = 0.22;
		double raw = baseTheta + k * Math.Log10(Math.Max(1, n));
		raw *= (0.9 + 0.2 * sepRatio);
		return Math.Clamp(raw, 0.6, 1.2);
	}

	#endregion
}