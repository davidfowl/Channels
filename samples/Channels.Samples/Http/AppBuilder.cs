using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Samples.Http
{
    public class AppBuilder
    {
        private readonly List<Func<RequestDelegate, RequestDelegate>> _middlewares = new List<Func<RequestDelegate, RequestDelegate>>();

        public void Use(Func<RequestDelegate, RequestDelegate> middleware)
        {
            _middlewares.Add(middleware);
        }

        public void Run(RequestDelegate callback)
        {
            Use(next => callback);
        }

        public RequestDelegate Build()
        {
            RequestDelegate app = ctx =>
            {
                ctx.StatusCode = 404;
                return Task.CompletedTask;
            };

            _middlewares.Reverse();

            foreach (var m in _middlewares)
            {
                app = m(app);
            }

            return app;
        }
    }
}
