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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Template;
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

        private HttpContext _resolvVirtualPathContext;
        
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
            var factory = _host.Services.GetService<IHttpContextFactory>();
            _resolvVirtualPathContext = factory.Create(_host.ServerFeatures);
            return;
        }

        public void Stop()
        {
            if (_resolvVirtualPathContext != null)
            {
                var factory = _host.Services.GetService<IHttpContextFactory>();
                factory.Dispose(_resolvVirtualPathContext);
            }
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
        public static Expression ResolveTypecast(Type dst,Expression fromArg)
        {
            
            var typeConverter = TypeDescriptor.GetConverter(dst);
            Expression<Action> act = () => typeConverter.ConvertFrom(null);
            MethodInfo mi = ((MethodCallExpression) act.Body).Method;
            return Expression.Convert(Expression.Call(Expression.Constant(typeConverter), mi, fromArg),dst);
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

        public static Expression ContextGetQueryValue(ParameterInfo param, Expression httpContext)
        {
            //context.Request.Query[index]
            PropertyInfo request = typeof(HttpContext).GetProperty("Request");
            PropertyInfo query = request.PropertyType.GetProperty("Query");
            MethodInfo get_Item = query.PropertyType.GetMethod("get_Item",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var QueryExpr = Expression.Property(Expression.Property(httpContext, request), query);
            MethodInfo contains = query.PropertyType.GetMethod("ContainsKey",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var checkExpr = Expression.Call(QueryExpr, contains, Expression.Constant(param.Name));
            var getExpr = Expression.Call(QueryExpr, get_Item, Expression.Constant(param.Name));
            return Expression.Condition(checkExpr, getExpr,
                Expression.Constant(param.HasDefaultValue ? param.DefaultValue : GetDefaultValue(param.ParameterType)));
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

        private static Action<HttpContext> BuildHandler(MethodInfo mi,object target,string routeParameter)
        {
            var template = TemplateParser.Parse(routeParameter);
            var context = Expression.Parameter(typeof(HttpContext));
            var parameters = mi.GetParameters().Select(para => ResolveTypecast(para.ParameterType,
                (template.Parameters.FirstOrDefault(ele => ele.Name == para.Name) != default(TemplatePart))
                    ? ContextGetRouteValue(para, context)
                    : ContextGetQueryValue(para, context)));
            var expr = Expression.Call(Expression.Constant(target), mi, parameters);
            var callLambda = Expression.Lambda(expr, context).Compile();
            if (mi.ReturnType != typeof(void))
            {
                return BuildReturnValueResolver(callLambda);

            }
            return (Action<HttpContext>)callLambda;
        }

        private static Action<HttpContext> BuildHandler(Delegate handler, string routeParameter)
        {
            var template = TemplateParser.Parse(routeParameter);
            //ummmm还有返回值的转换来着，忘记做了
            var context = Expression.Parameter(typeof(HttpContext));
            var parameters = handler.Method.GetParameters().Select(para =>
            {
                if (para.ParameterType == typeof(HttpContext))
                {
                    return context;
                }

                return ResolveTypecast(para.ParameterType,
                    (template.Parameters.FirstOrDefault(ele => ele.Name == para.Name) != default(TemplatePart))
                        ? ContextGetRouteValue(para, context)
                        : ContextGetQueryValue(para, context));
            });
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
                    var routeName = $"{srv.Alias}-{methodInfo.Name}";
                    var templateStr = MixRoute(srv.Alias, info.Template);
                    if (func == null)
                        func = BuildHandler(methodInfo, srv, templateStr);
                    var constraints =
                        new RouteValueDictionary(new {httpMethod = new HttpMethodRouteConstraint(info.Verb)});
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
                        routeName:routeName,
                        routeTemplate: templateStr,
                        defaults: null,
                        constraints: constraints,
                        dataTokens: null,
                        inlineConstraintResolver: inlineResolver);
                    
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

        public void RemoveRoute(ISTKService srv)
        {
            _router.RemoveRouting(srv);
        }

        public string GetVirtualPath(ISTKService srv, MethodInfo mi, RouteValueDictionary args)
        {
            if (_resolvVirtualPathContext == null)
            {
                var factory = _host.Services.GetService<IHttpContextFactory>();
                _resolvVirtualPathContext = factory.Create(_host.ServerFeatures);
            }
            var vpc = new VirtualPathContext(_resolvVirtualPathContext, null, args, $"{srv.Alias}-{mi.Name}");
            var path = _router.GetVirtualPath(vpc).VirtualPath;
            return "";
        }
    }
}
