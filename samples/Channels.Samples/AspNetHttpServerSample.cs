using System;
using System.Collections.Generic;
using System.Text;
using Channels.Samples.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Channels.Samples
{
    public class AspNetHttpServerSample
    {
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
                                    var view = @"
<html>
<head>
<title></title>
</head>
<body>
<form action="""" method=""post"">
    Username:<input type=""text"" name=""username"">
    Password:<input type=""password"" name=""password"">
    <input type=""submit"" value=""Login"">
</form>
</body>
</html>";
                                    app.Run(async context =>
                                    {
                                        if (!string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var contentLength = context.Request.ContentLength;

                                            // Access the raw connection
                                            var connection = context.Features.Get<ITcpConnectionFeature>();
                                            var data = new Dictionary<string, StringValues>();

                                            // Reads the form body
                                            while (true)
                                            {
                                                var buffer = await connection.Input.ReadAsync();

                                                try
                                                {
                                                    if (buffer.IsEmpty && connection.Input.Completion.IsCompleted)
                                                    {
                                                        // Connection closed
                                                        return;
                                                    }

                                                    if (FormReader.TryParse(ref buffer, ref data, ref contentLength))
                                                    {
                                                        break;
                                                    }
                                                }
                                                finally
                                                {
                                                    buffer.Consumed();
                                                }
                                            }

                                            foreach (var item in data)
                                            {
                                                Console.WriteLine($"{item.Key}={item.Value}");
                                            }

                                            // This is the idiomatic approach
                                            // var form = await context.Request.ReadFormAsync();
                                        }
                                        context.Response.ContentLength = view.Length;
                                        await context.Response.WriteAsync(view);
                                    });
                                })
                                .Build();
            host.Run();
        }
    }
}
