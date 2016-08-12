using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Channels.Samples.Http;
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
                                    await context.Response.WriteAsync("Hello World");
                                });
                            })
                            .Build();
            host.Run();
        }
    }
}
