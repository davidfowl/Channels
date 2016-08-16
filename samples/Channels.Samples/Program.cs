using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Channels.Samples.Http;
using Channels.Samples.IO;
using Channels.Samples.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // RunHttpServer();
            // RunCompressionSample();
            int depth = 5;
            int calls = 1000000;
            TestCallbacks(depth, calls);
            TestAsyncCallbacks(depth, calls);
        }

        private static void TestCallbacks(int depth, int n)
        {
            var o = new ObjectWithCallback();
            o.Register(End, null);

            for (int i = 0; i < depth - 1; i++)
            {
                var next = new ObjectWithCallback();
                next.Register(Continuation, o);
                o = next;
            }

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                o.Run();
            }
            sw.Stop();
            Console.WriteLine($"Did {n} callbacks call depth {depth} in {sw.ElapsedMilliseconds}ms");
        }

        private static void Continuation(object state)
        {
            ((ObjectWithCallback)state).Run();
        }

        private static void End(object state)
        {

        }

        private static void TestAsyncCallbacks(int depth, int n)
        {
            var o = new AwaitableObject();
            var t = End(o);

            for (int i = 0; i < depth - 1; i++)
            {
                var next = new AwaitableObject();
                t = Consume(next, o);
                o = next;
            }

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                o.Run();
            }
            Console.WriteLine($"Did {n} async callbacks with call depth {depth} in {sw.ElapsedMilliseconds}ms");
        }

        private static async Task Consume(AwaitableObject o1, AwaitableObject o2)
        {
            while (true)
            {
                await o1;
                o2.Run();
            }
        }

        private static async Task End(AwaitableObject o3)
        {
            while (true)
            {
                await o3;
            }
        }

        public class AwaitableObject : ICriticalNotifyCompletion
        {
            private Action _continuation;
            private static Action _callbackRan = () => { };

            public bool IsCompleted => _continuation == _callbackRan;
            public AwaitableObject GetAwaiter() => this;

            public void GetResult()
            {
                _continuation = null;
            }

            public void OnCompleted(Action continuation)
            {
                if (_continuation == _callbackRan ||
                    Interlocked.CompareExchange(ref _continuation, continuation, null) == _callbackRan)
                {
                    Task.Run(continuation);
                }
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                OnCompleted(continuation);
            }

            public void Run()
            {
                var c = (_continuation ?? Interlocked.CompareExchange(ref _continuation, _callbackRan, null));
                if (c != null)
                {
                    c();
                }
            }
        }

        public class ObjectWithCallback
        {
            private Action<object> _action;
            private object _state;

            public void Register(Action<object> action, object state)
            {
                _action = action;
                _state = state;
            }

            public void Run()
            {
                _action(_state);
            }
        }

        private static void RunCompressionSample()
        {
            using (var pool = new MemoryPool())
            {
                var channelFactory = new ChannelFactory(pool);

                var filePath = Path.GetFullPath("Program.cs");

                //var fs = File.OpenRead(filePath);
                //var compressed = new MemoryStream();
                //var compressStream = new DeflateStream(compressed, CompressionMode.Compress);
                //fs.CopyTo(compressStream);
                //compressStream.Flush();
                //compressed.Seek(0, SeekOrigin.Begin);
                // var input = channelFactory.MakeReadableChannel(compressed);

                var fs1 = File.OpenRead(filePath);
                var input = channelFactory.MakeReadableChannel(fs1);
                input = channelFactory.CreateDeflateCompressChannel(input, CompressionLevel.Optimal);

                input = channelFactory.CreateDeflateDecompressChannel(input);

                // Wrap the console in a writable channel
                var output = channelFactory.MakeWriteableChannel(Console.OpenStandardOutput());

                // Copy from the file channel to the console channel
                input.CopyToAsync(output).GetAwaiter().GetResult();

                input.CompleteReading();

                output.CompleteWriting();

                Console.ReadLine();
            }
        }

        private static void RunHttpServer()
        {
            var host = new WebHostBuilder()
                                        .ConfigureServices(services =>
                                        {
                                            // Use a custom server
                                            services.AddTransient<IServer, HttpServer>();
                                        })
                                        .Configure(app =>
                                        {
                                            app.Run(async context =>
                                            {
                                                context.Response.ContentLength = 11;
                                                await context.Response.WriteAsync("Hello World");
                                            });
                                        })
                                        .Build();
            host.Run();
        }
    }
}
