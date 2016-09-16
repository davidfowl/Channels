using System.Text;
using Channels.Samples.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Channels.Samples
{
    public class AspNetHttpServerSample
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

        public static void Run()
        {
            var host = new WebHostBuilder()
                                .UseUrls("http://*:5000")
                                .ConfigureServices(services =>
                                {
                                    // Use a custom server
                                    services.AddTransient<IServer, HttpServer>();
                                })
                                // .UseKestrel()
                                .Configure(app =>
                                {
                                    app.Run(context =>
                                    {
                                        context.Response.StatusCode = 200;
                                        context.Response.ContentType = "text/plain";
                                        // HACK: Setting the Content-Length header manually avoids the cost of serializing the int to a string.
                                        //       This is instead of: httpContext.Response.ContentLength = _helloWorldPayload.Length;
                                        context.Response.Headers["Content-Length"] = "13";
                                        return context.Response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldPayload.Length);
                                    });
                                })
                                .Build();
            host.Run();
        }
    }
}