using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace STKProject
{
    public class NetworkManager : ISTKService
    {
        public string Alias { get; set; }

        private bool _status = false;

        public string ListenUrl { get; set; }

        public bool UseSSL { get; set; }

        public IWebHost _host;

        private CancellationToken _token;

        private Task _svrTask;

        private VariableRouterCollection _router;

        protected class NetworkStartup : IStartup
        {

            public RouteCollection router { get; private set; }
            public Func<HttpContext, Task> DefaultHandler;
            public bool Configured { get; set; }

            public NetworkStartup()
            {
                Console.WriteLine("init");
            }

            public void Configure(IApplicationBuilder app)
            {
                Configure(app, app.ApplicationServices.GetService<ILoggerFactory>());
            }

            // Routes must configured in Configure
            public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
            {
                VariableRouterCollection routerCollection = new VariableRouterCollection();

                //var routes = routeBuilder.Build();
                //router = routes as RouteCollection;
                app.UseRouter(routerCollection);
                router = routerCollection;
                Configured = true;

                // Show link generation when no routes match.
                app.Run(async (context) =>
                {
                    if (DefaultHandler != null)
                    {
                        await DefaultHandler(context);
                        return;
                    }
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("404 - Page Not Found.");
                });
                // End of app.Run
            }

            IServiceProvider IStartup.ConfigureServices(IServiceCollection services)
            {
                services.AddRouting();
                return services.BuildServiceProvider();
            }
        }

        public void Start()
        {
            if (_status) return;
            _status = true;
            _token = new CancellationToken(false);
            _host = new WebHostBuilder()
                .ConfigureServices(srv => { srv.AddRouting(); })
                .Configure(app =>
                {
                    _router = new VariableRouterCollection();
                    app.UseRouter(_router);
                    app.Run(async (context) =>
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("404 - Page Not Found.");
                    });
                })
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseUrls(ListenUrl)
                .Build();
            _svrTask = _host.StartAsync(_token);
            _svrTask.Wait();
            return;
        }

        public void Stop()
        {

        }

        public void LoadDefaultSetting()
        {
            Alias = "NetworkManager";
            UseSSL = false;
            ListenUrl = "http://*:80";
        }
        /// <summary>
        /// Returns a expression : typeConverter.ConvertFrom(fromData) 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="fromArg"></param>
        /// <returns></returns>
        private static Expression ResolveTypecast(Type dst,Expression fromArg)
        {
            
            var typeConverter = TypeDescriptor.GetConverter(dst);
            Expression<Action> act = () => typeConverter.ConvertFrom(null);
            MethodInfo mi = ((MethodCallExpression) act.Body).Method;
            return Expression.Call(Expression.Constant(typeConverter), mi, fromArg);
        }
        /// <summary>
        /// Get default value of specfied type.
        /// Ref : https://stackoverflow.com/questions/1281161/how-to-get-the-default-value-of-a-type-if-the-type-is-only-known-as-system-type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        /// <summary>
        /// Returns a delegate(Expression) : (httpContext) => httpContext.GetRouteData().Values.containsKey(
        /// </summary>
        /// <param name="param"></param>
        /// <param name="httpContext"></param>
        /// <returns></returns>
        public static Expression ContextGetRouteValue(ParameterInfo param, Expression httpContext)
        {
            //总觉得能够再简化的...我觉得是对限定条件的明确出了点问题，因为从route那边来的，只有简单类型啊

            //说起来拿委托还是照样做...拿Expression大概只能给自己一个能够少一次call的优化错觉（骗自己）
            //此处假设所有绑定的属性都已经存在了
            var memberNameExpr = Expression.Constant(param.Name);
            //(null as HttpContext).GetRouteData().Values[]
            Expression<Func<RouteData>> act = () => (null as HttpContext).GetRouteData();
            MethodInfo mi = ((MethodCallExpression) act.Body).Method;
            PropertyInfo pi = mi.ReturnType.GetProperty("Values");
            var routeValueExpr = Expression.Property(Expression.Call(mi,httpContext), pi);
            MethodInfo getItem = pi.PropertyType.GetMethod("get_Item",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var GetValueExpr = Expression.Call(routeValueExpr, getItem, memberNameExpr);
            return GetValueExpr;
        }

        private static Action<HttpContext> SimpleTypeResolver<T>(Func<HttpContext, T> func)
        {
            
            return context =>
            {
                var result = func(context).ToString();
                Console.WriteLine(result);
                var txt = result;
                var arr = Encoding.UTF8.GetBytes(txt);
                context.Response.Body.Write(arr,0,arr.Length);
                //context.Response.WriteAsync().Wait();
            };
        }

        private static Action<HttpContext> BuildReturnValueResolver(Delegate func)
        {
            //直接用委托的话在编译时不方便确定类型，要把类型检测丢到运行时去......
            //Expression没那么方便.ummm记得有C++还是C#有泛型lambda了，去看看
            var returnType = func.Method.ReturnType;
            var isComplexFunc = !(TypeDescriptor.GetConverter(returnType).CanConvertFrom(typeof(string)));
            if (!isComplexFunc)
            {
                Expression<Action> expr = () => SimpleTypeResolver<int>(null as Func<HttpContext, int>);
                var method = (expr.Body as MethodCallExpression).Method.GetGenericMethodDefinition()
                    .MakeGenericMethod(returnType);
                return (Action<HttpContext>) method.Invoke(null, new object[]{func});
            }
            else
            {
                var xfunc = (Func<HttpContext, string>) func;
                return context =>
                {
                    var result = func.DynamicInvoke(context);
                    string str = JsonConvert.SerializeObject(result,Formatting.None);
                    var txt = JsonConvert.SerializeObject(result,Formatting.None);
                    var arr = Encoding.UTF8.GetBytes(txt);
                    context.Response.Body.Write(arr,0,arr.Length);
                };
            }
        }

        private static Action<HttpContext> BuildHandler(MethodInfo mi,object target)
        {
            var context = Expression.Parameter(typeof(HttpContext));
            var parameters = mi.GetParameters().Select(para => ResolveTypecast(para.ParameterType, ContextGetRouteValue(para, context)));
            var expr = Expression.Call(Expression.Constant(target), mi, parameters);
            var callLambda = Expression.Lambda(expr, context).Compile();
            if (mi.ReturnType != typeof(void))
            {
                return BuildReturnValueResolver(callLambda);

            }
            return (Action<HttpContext>)callLambda;
        }

        private static Action<HttpContext> BuildHandler(Delegate handler)
        {
            //ummmm还有返回值的转换来着，忘记做了
            var context = Expression.Parameter(typeof(HttpContext));
            var parameters = handler.Method.GetParameters().Select(para => ResolveTypecast(para.ParameterType,ContextGetRouteValue(para, context)));
            var expr = Expression.Invoke(Expression.Constant(handler), parameters);
            var callLambda = Expression.Lambda(expr, context).Compile();
            if (handler.Method.ReturnType != typeof(void))
            {
                return BuildReturnValueResolver(callLambda);

            }
            return (Action<HttpContext>)callLambda;
        }

        private static Func<HttpContext,Task> PackToTask(Action<HttpContext> func)
        {
            return context => new Task(() => { func(context); });
        }

        public void ResolveRouter(ISTKService srv)
        {
            ServiceRouteCollection routers = new ServiceRouteCollection();
            var inlineResolver = _host.Services.GetService<IInlineConstraintResolver>();
            Type srvType = srv.GetType();
            foreach (var methodInfo in srvType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Action<HttpContext> func = null;
                foreach (var info in methodInfo.GetCustomAttributes(false).OfType<RouteAttribute>())
                {
                    if(func == null)
                        func = BuildHandler(methodInfo, srv);
                    var route = new Route(
                        new RouteHandler(context =>
                        {
                            var ret = new Task(() =>
                            {
                                try
                                {
                                    func(context);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
                            });
                            ret.Start();
                            return ret;
                        }),
                        MixRoute(srv.Alias,info.Template),
                        defaults: null,
                        constraints: new RouteValueDictionary(new { httpMethod = new HttpMethodRouteConstraint(info.Verb) }),
                        dataTokens: null,
                        inlineConstraintResolver: inlineResolver);
                    foreach (var param in methodInfo.GetParameters().Select(param=>param.Name))
                    {
                        if (route.ParsedTemplate.Parameters.First(template => template.Name == param) == null)
                        {
                            throw new ArgumentException($"Error : Can not match parameter {param} to route");
                        }
                    }
                    routers.Add(route);
                }
            }

            routers.Service = srv;
            _router.Add(routers);
        }

        private static string MixRoute(string left, string right)
        {
            return NormalizeRoute(left) + "/" + NormalizeRoute(right);
        }

        private static string NormalizeRoute(string source)
        {
            if (source == null)
                return null;
            var result = source.Replace('\\', '/');
            if (result.Length > 0 && result.First() == '/')
                return result.Substring(1);
            return result;
        }

        
    }
}
