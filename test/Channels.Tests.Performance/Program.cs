using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Channels.Tests.Performance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var options = (uint[])Enum.GetValues(typeof(BenchmarkType));
            BenchmarkType type;
            if (args.Length != 1 || !Enum.TryParse(args[0], out type))
            {
                Console.WriteLine($"Please add benchmark to run as parameter:");
                for (var i = 0; i < options.Length; i++)
                {
                    Console.WriteLine($"  {((BenchmarkType)options[i]).ToString()}" );
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

            if (type.HasFlag(BenchmarkType.TrySliceTo))
            {
                BenchmarkRunner.Run<TrySliceToBenchmark>();
            }
        }
    }

    [Flags]
    public enum BenchmarkType : uint
    {
        Streams = 1,
        TrySliceTo = 2,
        // add new ones in powers of two - e.g. 2,4,8,16...

        All = uint.MaxValue
    }
}

