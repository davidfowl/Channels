using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Channels.Samples.Http;
using Channels.Samples.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Channels.Samples
{
    public class AspNetHttpServerSample
    {
        private static readonly JsonSerializer _json = new JsonSerializer();
        private static readonly UTF8Encoding _utf8Encoding = new UTF8Encoding(false);
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
                                        if (context.Request.Path.StartsWithSegments("/plaintext"))
                                        {
                                            context.Response.StatusCode = 200;
                                            context.Response.ContentType = "text/plain";
                                            // HACK: Setting the Content-Length header manually avoids the cost of serializing the int to a string.
                                            //       This is instead of: httpContext.Response.ContentLength = _helloWorldPayload.Length;
                                            context.Response.Headers["Content-Length"] = "13";
                                            return context.Response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldPayload.Length);

                                        }
                                        else if (context.Request.Path.StartsWithSegments("/json"))
                                        {
                                            context.Response.ContentType = "application/json";
                                            context.Response.ContentLength = 3072;

                                            var model = BigModels.About100Fields;

                                            var outputWriter = new StreamWriter(context.Response.Body, _utf8Encoding);
                                            var jsonWriter = new JsonTextWriter(outputWriter);
                                            _json.Serialize(outputWriter, model);
                                            jsonWriter.Flush();
                                            outputWriter.Flush();
                                        }

                                        return Task.CompletedTask;
                                    });
                                })
                                .Build();
            host.Run();
        }
    }
}