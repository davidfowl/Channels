using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Channels.Tests.Performance
{
    public class DefaultConfig : ManualConfig
    {
        public DefaultConfig()
        {
            Add(Job.Default.
                With(Platform.X64).
                With(Jit.RyuJit).
                With(Runtime.Clr).
                WithLaunchCount(3).
                WithIterationTime(200). // 200ms per iteration
                WithWarmupCount(5).
                WithTargetCount(10));

            Add(new BenchmarkDotNet.Diagnostics.Windows.MemoryDiagnoser());
        }
    }

    public class NoMemoryConfig : ManualConfig
    {
        public NoMemoryConfig()
        {
            Add(Job.Default.
                With(Platform.X64).
                With(Jit.RyuJit).
                With(Runtime.Clr).
                WithLaunchCount(3).
                WithIterationTime(200). // 200ms per iteration
                WithWarmupCount(5).
                WithTargetCount(10));
        }
    }
}
