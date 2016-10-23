using System;
using System.Diagnostics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Channels.Tests.Performance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            RunSelectedBenchmarks(BenchmarkType.OpenSsl);
            return;
            var options = (uint[])Enum.GetValues(typeof(BenchmarkType));
            BenchmarkType type;
            if (args.Length != 1 || !Enum.TryParse(args[0], out type))
            {
                Console.WriteLine($"Please add benchmark to run as parameter:");
                for (var i = 0; i < options.Length; i++)
                {
                    Console.WriteLine($"  {((BenchmarkType)options[i]).ToString()}");
                }

                return;
            }

            RunSelectedBenchmarks(type);
        }

        private static void RunSelectedBenchmarks(BenchmarkType type)
        {
            if (type.HasFlag(BenchmarkType.Streams))
            {
                BenchmarkRunner.Run<ChannelsStreamsBenchmark>();
            }
            if(type.HasFlag(BenchmarkType.OpenSsl))
            {
                //if (Debugger.IsAttached)
                //{
                    TlsDataEncryptBenchmark.Setup();
                //    for (int i = 0; i < 100; i++)
                //    {
                //        TlsDataEncryptBenchmark.OpenSslChannelAllTheThings();
                //    }
                //}
                //else
                //{
                //    BenchmarkRunner.Run<TlsDataEncryptBenchmark>();
                //}
            }
        }
    }

    [Flags]
    public enum BenchmarkType : uint
    {
        Streams = 1,
        // add new ones in powers of two - e.g. 2,4,8,16...
        OpenSsl = 2,
        All = uint.MaxValue
    }

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

            Add(new MemoryDiagnoser());
        }
    }

    public class LargerTestConfig : ManualConfig
    {
        public LargerTestConfig()
        {
            Add(Job.Default.
                With(Platform.X64).
                With(Jit.RyuJit).
                With(Runtime.Clr).
                WithLaunchCount(3).
                WithWarmupCount(2).
                WithTargetCount(10));

            Add(new MemoryDiagnoser());
        }
    }
}

