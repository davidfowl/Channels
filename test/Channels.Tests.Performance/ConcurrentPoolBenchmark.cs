using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Jobs;

namespace Channels.Tests.Performance
{
    [Config(typeof(Config))]
    public class ConcurrentPoolBenchmark
    {
        public class Config : ManualConfig
        {
            public Config()
            {
                Add(Job.Default.
                    With(Platform.X64).
                    With(Jit.RyuJit).
                    With(Runtime.Clr).
                    WithIterationTime(200). // 200ms per iteration
                    WithWarmupCount(5).
                    WithTargetCount(10));
                Add(new MemoryDiagnoser());
            }
        }

        public const int InnerLoopCount = 100;
        const int Count = 4096;

        public ChannelFactory ChannelFactory;
        public Poolable.ConcurrentPool<Poolable> Pool;
        public ConcurrentQueue<Poolable> BaselineQueue;

        [Setup]
        public void Setup()
        {
            if (Pool == null)
            {
                Pool = new Poolable.ConcurrentPool<Poolable>();
                // preallocate
                for (var i = 0; i < Count; i++)
                {
                    var poolable = new Poolable();
                    Pool.RegisterForPooling(poolable);
                    Pool.Enqueue(poolable);
                }

                BaselineQueue = new ConcurrentQueue<Poolable>();

                // preallocate
                for (var i = 0; i < Count; i++)
                {
                    BaselineQueue.Enqueue(new Poolable());
                }

            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void Leases()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                Poolable item;
                if (Pool.TryDequeue(out item))
                {
                    Pool.Enqueue(item);
                }
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount, Baseline = true)]
        public void BaseLine()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                Poolable item;
                if (BaselineQueue.TryDequeue(out item))
                {
                    BaselineQueue.Enqueue(item);
                }
            }
        }
    }
}