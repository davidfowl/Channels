using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Channels.Samples.Http;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            HttpServer.Listen(5000, app =>
            {
                app.Run(async context =>
                { 
                    var data = Encoding.UTF8.GetBytes("Hello World");
                    await context.Output.WriteAsync(data, 0, data.Length);
                });
            });

            Console.WriteLine("Listening on port 5000");
            var wh = new ManualResetEventSlim();
            Console.CancelKeyPress += (sender, e) =>
            {
                wh.Set();
            };
            wh.Wait();
        }
    }
}
