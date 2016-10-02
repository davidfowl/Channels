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
                                        else if (context.Request.Path.StartsWithSegments("/jsonpool"))
                                        {
                                            context.Response.ContentType = "application/json";
                                            context.Response.ContentLength = 3072;

                                            var model = BigModels.About100Fields;

                                            var outputWriter = new HttpResponseStreamWriter(context.Response.Body, _utf8Encoding);
                                            var jsonWriter = new JsonTextWriter(outputWriter);
                                            jsonWriter.ArrayPool = new JsonArrayPool<char>(ArrayPool<char>.Shared);
                                            _json.Serialize(outputWriter, model);
                                            jsonWriter.Flush();
                                            outputWriter.Flush();
                                        }
                                        else if (context.Request.Path.StartsWithSegments("/jsonbuffered"))
                                        {
                                            context.Response.ContentType = "application/json";

                                            var model = BigModels.About100Fields;

                                            var ms = new MemoryStream();
                                            var outputWriter = new StreamWriter(ms, _utf8Encoding);
                                            var jsonWriter = new JsonTextWriter(outputWriter);
                                            _json.Serialize(outputWriter, model);

                                            context.Response.ContentLength = ms.Length;
                                            ms.Position = 0;
                                            return ms.CopyToAsync(context.Response.Body);
                                        }
                                        else if (context.Request.Path.StartsWithSegments("/jsonbufferedpool"))
                                        {
                                            context.Response.ContentType = "application/json";

                                            var model = BigModels.About100Fields;

                                            var ms = new MemoryStream();
                                            var outputWriter = new HttpResponseStreamWriter(ms, _utf8Encoding);
                                            var jsonWriter = new JsonTextWriter(outputWriter);
                                            jsonWriter.ArrayPool = new JsonArrayPool<char>(ArrayPool<char>.Shared);
                                            _json.Serialize(outputWriter, model);

                                            context.Response.ContentLength = ms.Length;
                                            ms.Position = 0;
                                            return ms.CopyToAsync(context.Response.Body);
                                        }
                                        // REVIEW: Echo is broken for now because of handling of content length
                                        else if (context.Request.Path.StartsWithSegments("/jsonecho"))
                                        {
                                            var inputReader = new StreamReader(context.Request.Body, _utf8Encoding);
                                            var jsonReader = new JsonTextReader(inputReader);

                                            Pet value = _json.Deserialize<Pet>(jsonReader);

                                            var outputWriter = new StreamWriter(context.Response.Body, _utf8Encoding);
                                            var jsonWriter = new JsonTextWriter(outputWriter);
                                            _json.Serialize(outputWriter, value);
                                            jsonWriter.Flush();
                                        }
                                        else if (context.Request.Path.StartsWithSegments("/jsonechopool"))
                                        {
                                            var inputReader = new StreamReader(context.Request.Body, _utf8Encoding);
                                            var jsonReader = new JsonTextReader(inputReader);
                                            jsonReader.ArrayPool = new JsonArrayPool<char>(ArrayPool<char>.Shared);

                                            Pet value = _json.Deserialize<Pet>(jsonReader);

                                            var outputWriter = new StreamWriter(context.Response.Body, _utf8Encoding);
                                            var jsonWriter = new JsonTextWriter(outputWriter);
                                            jsonWriter.ArrayPool = new JsonArrayPool<char>(ArrayPool<char>.Shared);
                                            _json.Serialize(outputWriter, value);
                                            jsonWriter.Flush();
                                        }

                                        return Task.CompletedTask;
                                    });
                                })
                                .Build();
            host.Run();
        }
    }
}