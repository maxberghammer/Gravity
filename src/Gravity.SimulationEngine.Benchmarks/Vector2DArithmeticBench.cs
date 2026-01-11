using System;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks
{
    [CPUUsageDiagnoser]
    [MemoryDiagnoser]
    public class Vector2DArithmeticBench
    {
        private Vector2D[] _vectors = null!;
        private double[] _scalars = null!;

        [Params(10_000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _vectors = new Vector2D[N];
            _scalars = new double[N];
            // Deterministic data for benchmark input
            for (int i = 0; i < N; i++)
            {
                double x = Math.Sin(i * 0.001) * 0.5 + 0.5;
                double y = Math.Cos(i * 0.001) * 0.5 + 0.5;
                _vectors[i] = new Vector2D(x, y);
                _scalars[i] = (x * 0.773 + y * 0.227);
            }
        }

        [Benchmark]
        public Vector2D MultiplyAndAdd()
        {
            var acc = Vector2D.Zero;
            for (int i = 0; i < 1000; i++)
            {
                int idx = i % N;
                acc += _vectors[idx] * _scalars[idx];
            }
            return acc;
        }

        [Benchmark]
        public double DotProducts()
        {
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                int idx = i % (N - 1);
                sum += _vectors[idx] * _vectors[idx + 1];
            }
            return sum;
        }
    }
}