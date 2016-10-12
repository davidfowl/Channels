using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Utf8;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Channels.Compression;
using Channels.File;

using JsonReader = System.Text.Json.JsonReader;

namespace Channels.Tests.Performance
{
    [Config(typeof(DefaultConfig))]
    public class ChannelsStreamsBenchmark
    {
        public const int InnerLoopCount = 50;

        public static MemoryPool Pool;
        public static ChannelFactory ChannelFactory;

        [Setup]
        public static void Setup()
        {
            if (Pool == null)
            {
                Pool = new MemoryPool();
                Pool.Lease(1024).Dispose();
            }

            if (ChannelFactory == null)
            {
                ChannelFactory = new ChannelFactory(Pool);
            }

        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public static void Channels()
        {
            ChannelsAsync().Wait();
        }

        private static async Task ChannelsAsync()
        {
            var cf = ChannelFactory;
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var input = cf.ReadFile("random.json.gz")
                                .GZipDecompress(cf)
                                .GZipCompress(cf, CompressionLevel.NoCompression)
                                .GZipDecompress(cf)
                                .GZipCompress(cf, CompressionLevel.NoCompression)
                                .GZipDecompress(cf)
                                .GZipCompress(cf, CompressionLevel.NoCompression)
                                .GZipDecompress(cf)
                                .GZipCompress(cf, CompressionLevel.NoCompression)
                                .GZipDecompress(cf)
                                .GZipCompress(cf, CompressionLevel.NoCompression)
                                .GZipDecompress(cf);

                var data = await input.ReadToEndAsync();

                //ParseJson(data);
                input.Advance(data.End);
            }
        }


        private static unsafe void ParseJson(ReadableBuffer buffer)
        {
            var length = buffer.Length;

            byte* b = stackalloc byte[length];
            buffer.CopyTo(new Span<byte>(b, length));
            var utf8Str = new Utf8String(new ReadOnlySpan<byte>(b, length));
            //Console.WriteLine(utf8Str.Length);

            var reader = new JsonReader(utf8Str);

            while (reader.Read())
            {
                var tokenType = reader.TokenType;
                switch (tokenType)
                {
                    case JsonReader.JsonTokenType.ObjectStart:
                    case JsonReader.JsonTokenType.ObjectEnd:
                    case JsonReader.JsonTokenType.ArrayStart:
                    case JsonReader.JsonTokenType.ArrayEnd:
                        //Console.WriteLine(tokenType);
                        break;
                    case JsonReader.JsonTokenType.Property:
                        var name = reader.GetName();
                        //Console.WriteLine(name);
                        var value = reader.GetValue();
                        //Console.WriteLine(value);
                        break;
                    case JsonReader.JsonTokenType.Value:
                        value = reader.GetValue();
                        //Console.WriteLine(value);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = InnerLoopCount)]
        public static void Streams()
        {
            StreamsAsync().Wait();
        }


        public static async Task StreamsAsync()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var mem1 = new MemoryStream();

                using (var filestream = new FileStream("random.json.gz", FileMode.Open, FileAccess.Read))
                using (var gzip0 = new GZipStream(filestream, CompressionMode.Decompress))
                {
                    using (var gzip1 = new GZipStream(mem1, CompressionLevel.NoCompression, true))
                    {
                        await gzip0.CopyToAsync(gzip1);
                    }
                }
                mem1.Seek(0, SeekOrigin.Begin);

                var mem3 = new MemoryStream();
                using (var gzip2 = new GZipStream(mem1, CompressionMode.Decompress, true))
                {
                    using (var gzip3 = new GZipStream(mem3, CompressionLevel.NoCompression, true))
                    {
                        await gzip2.CopyToAsync(gzip3);
                    }
                }
                mem3.Seek(0, SeekOrigin.Begin);

                var mem5 = new MemoryStream();
                using (var gzip4 = new GZipStream(mem3, CompressionMode.Decompress, true))
                {
                    using (var gzip5 = new GZipStream(mem5, CompressionLevel.NoCompression, true))
                    {
                        await gzip4.CopyToAsync(gzip5);
                    }
                }
                mem5.Seek(0, SeekOrigin.Begin);

                var mem7 = new MemoryStream();
                using (var gzip6 = new GZipStream(mem5, CompressionMode.Decompress, true))
                {
                    using (var gzip7 = new GZipStream(mem7, CompressionLevel.NoCompression, true))
                    {
                        await gzip6.CopyToAsync(gzip7);
                    }
                }
                mem7.Seek(0, SeekOrigin.Begin);

                var mem9 = new MemoryStream();
                using (var gzip8 = new GZipStream(mem7, CompressionMode.Decompress, true))
                {
                    using (var gzip9 = new GZipStream(mem9, CompressionLevel.NoCompression, true))
                    {
                        await gzip8.CopyToAsync(gzip9);
                    }
                }
                mem9.Seek(0, SeekOrigin.Begin);

                using (var gzip10 = new GZipStream(mem9, CompressionMode.Decompress, true))
                {
                    using (var textReader = new StreamReader(gzip10))
                    {
                        var data = await textReader.ReadToEndAsync();

                        // Do stuff with data
                    }
                }
            }
        }
    }
}
