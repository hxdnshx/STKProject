using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace STKProject
{
    public class UseBuilderAttribute : Attribute
    {
        public Type BuilderType { get; set; }

        public UseBuilderAttribute(Type builderType)
        {
            this.BuilderType = builderType;
        }
    }

    public class ServiceManager : ISTKService
    {
        public string SettingPath = "ServiceSetting.json";

        private static Dictionary<int, Type> _actionTypes = new Dictionary<int, Type>();

        [NotMapped]
        public CancellationTokenSource TerminateToken { get; set; }

        public ServiceManager()
        {
            TerminateToken = new CancellationTokenSource();
        }

        /// <summary>
        /// 因为Action<>根据不同的泛型参数个数，对应的是不同的类型，所以需要获取一下w
        /// </summary>
        private void EnumActions()
        {
            _actionTypes.Clear();
            _actionTypes[0] = typeof(Action);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.FullName.Contains("System.Action") && type.IsGenericTypeDefinition)
                    {
                        //Console.WriteLine(type.ToString());
                        _actionTypes[type.GetGenericArguments().Length] = type;
                    }
                }
            }
        }

        public string Alias { get; set; }
        public void Start()
        {
            foreach (var srv in _activeServices)
            {
                if(srv.Instance != this)
                    srv.Instance.Start();
            }
        }

        public void Stop()
        {
            foreach (var srv in _activeServices)
            {
                if (srv.Instance != this)
                    srv.Instance.Stop();
            }
        }

        public void LoadDefaultSetting()
        {
            Alias = "ServiceManager";
        }

        private List<ServiceContext> _activeServices = new List<ServiceContext>();

        private List<Connection> _pendingConnect = new List<Connection>();

        private class ServiceContext
        {
            public ISTKService Instance;
            public string Scope;
            public List<Connection> Connections = new List<Connection>();
        }

        private class Connection
        {
            private ServiceContext source;
            private ServiceContext destination;
            public PropertyInfo TargetDelegate;
            public MethodInfo TargetMethod;
            private Delegate _connectionDelegate;
            private Delegate _method;
            private bool _isConnected;

            /// <summary>
            /// 为这个属性赋值时会自动加入Connection列表
            /// </summary>
            public ServiceContext Source
            {
                get => source;
                private set
                {
                    value.Connections.Add(this);
                    source = value;
                }
            }

            /// <summary>
            /// 在为这个属性赋值时会自动加入Connection列表
            /// </summary>
            public ServiceContext Destination
            {
                get => destination;
                private set
                {
                    value.Connections.Add(this);
                    destination = value;
                }
            }

            public void Destroy()
            {
                Destination.Connections.Remove(this);
                Source.Connections.Remove(this);
            }

            public Connection(ServiceContext src,ServiceContext dst, string srcDelegate,string dstMethod)
            {
                if (src.Instance.GetType().GetProperty(srcDelegate) == null)
                {
                    Destination = src;
                    Source = dst;
                    ResolveTarget(dstMethod,srcDelegate);
                    Console.WriteLine($"Warning : Reversed Delegate -> Method Order - {(src.Instance.Alias)}.{srcDelegate} -> {dst.Instance.Alias}.{dstMethod}");
                }
                else
                {
                    Source = src;
                    Destination = dst;
                    ResolveTarget(srcDelegate,dstMethod);
                }
                _isConnected = false;
            }

            private void ResolveTarget(string srcDelegate,string dstMethod)
            {
                TargetDelegate = Source.Instance.GetType().GetProperty(srcDelegate);
                if (TargetDelegate == null)
                {
                    Console.WriteLine($"Error : Can not found {Source.Instance.Alias}.{srcDelegate}");
                    throw new Exception($"Error : Can not found {Source.Instance.Alias}.{srcDelegate}");
                }

                TargetMethod = Destination.Instance.GetType().GetMethod(dstMethod,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (TargetMethod == null)
                {
                    Console.WriteLine($"Error : Can not found {Destination.Instance.Alias}.{dstMethod}");
                    throw new Exception($"Error : Can not found {Destination.Instance.Alias}.{dstMethod}");
                }

                _method = TargetMethod.CreateDelegate(typeof(Delegate), Destination.Instance);
            }

            private static bool IsFunc(MethodInfo mi)
            {
                return mi.ReturnType != typeof(void);
            }

            private static Delegate BuildAction(MethodInfo mi,object instance)
            {
                var parameters = mi.GetParameters().Select(param => Expression.Parameter(param.ParameterType));
                return Expression.Lambda(Expression.Call(Expression.Constant(instance), mi, parameters)).Compile();
            }

            public void Connect()
            {
                if (_isConnected)
                    return;
                if (Source == null || Destination == null)
                {
                    throw new Exception($"Error : Connection Target = null");
                }
                var fromMethodInfo = TargetMethod;
                var toPortInfo = TargetDelegate;
                var fromParameterInfo = fromMethodInfo.GetParameters();
                var toParameterInfo = toPortInfo.PropertyType.GetGenericArguments();
                for (int i = 0; i < fromParameterInfo.Length; i++)
                {
                    if (fromParameterInfo[i].ParameterType != toParameterInfo[i])
                        throw new Exception("Error : Method type not match.");
                }

                Delegate fromMethodDelegate;
                if(!IsFunc(fromMethodInfo))
                    fromMethodDelegate = Delegate.CreateDelegate(typeof(Delegate), Destination.Instance, fromMethodInfo, true);
                else
                {
                    fromMethodDelegate = BuildAction(fromMethodInfo, Destination.Instance);
                }
                _connectionDelegate = fromMethodDelegate;
                var toPortValue = (Delegate)toPortInfo.GetValue(Source.Instance);
                var finalDelegate = Delegate.Combine(fromMethodDelegate, toPortValue);
                toPortInfo.SetValue(Source.Instance, finalDelegate);
                _isConnected = true;
            }

            public void Disconnect()
            {
                if (!_isConnected)
                    return;
                var fromMethodInfo = TargetMethod;
                var toPortInfo = TargetDelegate;
                var toPortValue = (Delegate)toPortInfo.GetValue(Source.Instance);
                var finalDelegate = Delegate.Remove(toPortValue, _connectionDelegate);
                toPortInfo.SetValue(Source.Instance, finalDelegate);
                _isConnected = false;
            }
        }

        public class ServiceSetting
        {
            public class ConnectionInfo
            {
                public string Source { get; set; }
                public string Destination { get; set; }
            }
            public Dictionary<string, string> Properties { get; set; }
            public List<ConnectionInfo> Connections { get; set; }
            public string Scope { get; set; }
            public string Service { get; set; }
            [JsonIgnore] public Type ServiceType;
        }

        public bool ProcessSetting(List<ServiceSetting> setting)
        {
            bool isValid = true;
            foreach (var srv in setting)
            {
                if (!_serviceBaseInfo.ContainsKey(srv.Service))
                {
                    isValid = false;
                    Console.WriteLine($"Error : Unknown Service {srv.Service}");
                    continue;
                }
                if(srv.Properties == null)
                    srv.Properties = new Dictionary<string, string>();
                if(srv.Connections == null)
                    srv.Connections = new List<ServiceSetting.ConnectionInfo>();
                srv.ServiceType = _serviceBaseInfo[srv.Service];
                foreach (var srvProperty in srv.Properties.Keys)
                {
                    if (srv.ServiceType.GetProperty(srvProperty) == null)
                    {
                        Console.WriteLine($"Error : Unknown Property {srv.Service}.{srvProperty}");
                        isValid = false;
                    }
                }
            }

            return isValid;
        }

        public List<ServiceSetting> LoadSetting()
        {
            if (File.Exists(SettingPath) == false)
                return null;
            try
            {
                string str = File.ReadAllText(SettingPath);
                var setting = JsonConvert.DeserializeObject<List<ServiceSetting>>(str);
                return setting;
            }
            catch (Exception e)
            {
                Console.WriteLine("Error : Failed to parse setting file.");
            }

            return null;
        }

        public void SaveSetting(string path)
        {
            List<ServiceSetting> services = new List<ServiceSetting>();
            foreach (var srv in _activeServices)
            {
                ServiceSetting setting = new ServiceSetting();
                setting.Service = srv.Instance.GetType().Name;
                setting.Scope = srv.Scope;
                setting.ServiceType = srv.Instance.GetType();
                setting.Properties = new Dictionary<string, string>();
                foreach (var propertyInfo in srv.Instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var value = propertyInfo.GetValue(srv.Instance);
                    if(propertyInfo.GetCustomAttribute<NotMappedAttribute>(false) == null)
                        setting.Properties[propertyInfo.Name] = Convert.ToString(value);
                }
                setting.Connections = new List<ServiceSetting.ConnectionInfo>();
                foreach (var conn in srv.Connections)
                {
                    if (conn.Source.Instance != srv.Instance)
                        continue;
                    var src = conn.TargetDelegate.Name;
                    var dst = $"{conn.Destination.Instance.Alias}.{conn.TargetMethod.Name}";
                    setting.Connections.Add(new ServiceSetting.ConnectionInfo
                    {
                        Source = src,
                        Destination = dst,
                    });
                }
                services.Add(setting);
            }
            File.WriteAllText(path,JsonConvert.SerializeObject(services));
        }

        public void InitService(ServiceSetting setting)
        {
            if (setting.ServiceType == this.GetType())
                return;
            var context = new ServiceContext();
            context.Instance = _serviceTypeInfo[setting.ServiceType].Builder(this, setting.Scope) as ISTKService;
            context.Scope = setting.Scope;
            context.Instance.LoadDefaultSetting();
            foreach (var prop in setting.Properties)
            {
                var propInfo = setting.ServiceType.GetProperty(prop.Key);
                if (propInfo.PropertyType == typeof(bool))
                {
                    if (prop.Value == "True")
                    {
                        propInfo.SetValue(context.Instance, true);
                    }
                    else if(prop.Value == "False")
                    {
                        propInfo.SetValue(context.Instance,false);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid value : {propInfo.Name} <= {prop.Value}");
                    }
                }
                else
                {
                    propInfo.SetValue(context.Instance,Convert.ChangeType(prop.Value,propInfo.PropertyType));
                }
            }

            foreach (var conn in setting.Connections)
            {
                var dst = conn.Destination.Split('.');
                if (dst.Length != 2)
                    throw new Exception($"Error : Bad Connection String : {conn}");
                var dstService = GetServiceContext(dst[0], context.Scope);
                var connection = new Connection(context, dstService, conn.Source, dst[1]);
                if (dstService != null)
                {
                    connection.Connect();
                }
                else
                {
                    _pendingConnect.Add(connection);
                }
            }
            _activeServices.Add(context);
            //context.Instance.Start();
        }

        private void StopService(ServiceContext context)
        {
            if (context == null)
                throw new ArgumentException($"{nameof(context)} is null");
            context.Instance.Stop();
            foreach (var conn in context.Connections)
            {
                conn.Disconnect();
                conn.Destroy();
            }

            _activeServices.Remove(context);

        }

        public void StopService(string Alias, string scope = "")
        {
            StopService(GetServiceContext(Alias,scope));
        }

        public void Initialize()
        {
            LoadDefaultSetting();
            if(_serviceBaseInfo == null)
                InitializeServices();
            var srvList = LoadSetting();
            bool isDefaultSettingLoaded = false;
            if (srvList == null)
            {
                //Load Default Setting
                srvList=new List<ServiceSetting>
                {
                    new ServiceSetting
                    {
                        Service = nameof(NetworkManager), 
                        Scope = ""
                    },
                    new ServiceSetting
                    {
                        Service = nameof(DefaultLogger),
                        Scope = ""
                    }
                };
                isDefaultSettingLoaded = true;
            }

            Debug.Assert(_activeServices.Count == 0);
            _activeServices.Add(new ServiceContext
            {
                Instance = this,
                Scope = ""
            });

            ProcessSetting(srvList);

            //Sort Start Order
            Dictionary<ServiceSetting, List<Type>> depends = new Dictionary<ServiceSetting, List<Type>>();
            foreach (var srv in srvList)
            {
                depends[srv] = new List<Type>(_serviceTypeInfo[srv.ServiceType].Requirements);
            }

            for (;;)
            {
                if (depends.Count == 0)
                    break;
                bool started = false;
                foreach (var srv in depends)
                {
                    if (srv.Value.Count == 0)
                    {
                        InitService(srv.Key);
                        foreach (var otherSrv in depends)
                        {
                            if(otherSrv.Key == srv.Key)
                                continue;
                            otherSrv.Value.RemoveAll(t => IsType(srv.Key.ServiceType, t));
                        }

                        started = true;
                        depends.Remove(srv.Key);
                        break;
                    }
                }

                if (started == false)
                {
                    Console.WriteLine("Error : Cyclic Dependence, Load Default Service Setting.");
                    var tmp = SettingPath;
                    SettingPath = "InvalidNyanPants.Mock";
                    Initialize();
                    return;
                }
            }

            if (GetService<NetworkManager>() != null)
            {
                var network = GetService<NetworkManager>();
                network.Start();
                foreach (var srv in _activeServices)
                {
                    network.ResolveRouter(srv.Instance);
                }
            }
            if(isDefaultSettingLoaded)
                SaveSetting(SettingPath);
        }

        public static string GetExePath()
        {
            //Ref: https://stackoverflow.com/questions/6246074/mono-c-sharp-get-application-path
            var procPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            //FileInfo fi=new FileInfo(procPath);
            return procPath;
        }

        class DepInfo
        {
            public List<Type> Depends;
            public int Count;

            public DepInfo()
            {
                Depends = new List<Type>();
                Count = 0;
            }
        }

        class TypeExtInfo
        {
            public Func<ServiceManager, string, object> Builder;
            public List<Type> Requirements;
        }

        private Dictionary<string, Type> _serviceBaseInfo;
        private Dictionary<Type, TypeExtInfo> _serviceTypeInfo;

        public T GetService<T>(string scope = "")
        {
            return (T) GetService(typeof(T), scope);
        }

        public ISTKService GetService(Type serviceType, string scope = "")
        {
            return _activeServices.Where(srv => IsType(srv.Instance, serviceType))
                .First(srv => ScopeCheck(srv.Scope, scope))?.Instance;
        }

        private static bool IsType(object obj, Type check)
        {
            var objType = obj.GetType();
            return objType == check || objType.GetInterfaces().Contains(check);
        }

        public static bool IsType(Type src, Type check)
        {
            return src == check || src.GetInterfaces().Contains(check);
        }

        public IEnumerable<ISTKService> GetServices()
        {
            return _activeServices.Select(srv => srv.Instance);
        }

        public ISTKService GetService(string Alias, string scope = "")
        {
            return _activeServices.Where(srv => srv.Instance.Alias == Alias)
                .First(srv => ScopeCheck(srv.Scope, scope))
                ?.Instance;
        }

        private bool ScopeCheck(string source, string check)
        {
            var sourceList = NormalizeScope(source).Split('/');
            var checkList = NormalizeScope(check).Split('/');
            for (int i = 0; i < checkList.Length; ++i)
            {
                if (sourceList[i] != checkList[i])
                    return false;
            }

            return true;
        }

        private ServiceContext GetServiceContext(string Alias, string scope = "")
        {
            return _activeServices.Where(srv => srv.Instance.Alias == Alias)
                .First(srv => ScopeCheck(srv.Scope, scope));
        }

        private static string NormalizeScope(string source)
        {
            if (source == null)
                return null;
            var result = source.Replace('\\', '/');
            if (result.Length>0 && result.First() == '/')
                return result.Substring(1);
            return result;
        }

        private void InitializeServices()
        {
            List<Type> typeDep = new List<Type>();
            List<string> loadedAssemblyList = new List<string>();

            //Enumerate Services
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    loadedAssemblyList.Add(assembly.ManifestModule.Name);
                    //loadedAssemblyList.Add(assembly.);
                    foreach (var aType in assembly.GetTypes())
                    {
                        if (aType.GetInterfaces().Contains(typeof(ISTKService)))
                            typeDep.Add(aType);
                    }
                }
                var enumPath = GetExePath();
                Console.WriteLine("Load Assemblies in " + enumPath);
                var dllList = Directory.GetFiles(enumPath, "*.dll");
                foreach (var file in dllList)
                {
                    FileInfo fi = new FileInfo(file);
                    if (loadedAssemblyList.Contains(fi.Name)) continue;
                    Console.WriteLine("Found:" + fi.FullName);
                    Assembly assembly = null;
                    try
                    {
                        assembly = Assembly.LoadFrom(file);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Failed to Load Assembly:" + fi.FullName);
                        continue;
                        //Failed To Load Assembley
                    }

                    foreach (var aType in assembly.GetTypes())
                    {
                        if (aType.GetInterfaces().Contains(typeof(ISTKService)))
                            typeDep.Add(aType);
                    }
                }

                Console.WriteLine("Found STKServices:");
                foreach (var serviceType in typeDep)
                {
                    Console.WriteLine("Srv:" + serviceType);
                }
            }

            //Fill Type Info
            {
                if (_serviceBaseInfo == null)
                {
                    _serviceBaseInfo = new Dictionary<string, Type>();
                }
                if (_serviceTypeInfo == null)
                {
                    _serviceTypeInfo = new Dictionary<Type, TypeExtInfo>();
                }
                _serviceTypeInfo.Clear();
                _serviceBaseInfo.Clear();
                foreach (var depInfo in typeDep)
                {
                    _serviceBaseInfo[depInfo.Name] = depInfo;
                    if (depInfo.GetCustomAttribute<UseBuilderAttribute>() != null)
                    {
                        var builderType = depInfo.GetCustomAttribute<UseBuilderAttribute>().BuilderType;
                        var builderInfo = builderType.GetMethod("Build",
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (builderInfo == null)
                        {
                            Console.WriteLine($"Error : Can not find Build() in Builder Class {builderType}");
                        }
                        //TO Do：换成Expression形式的（不过这个构造函数只调用一次没必要这样优化啦
                        TypeExtInfo info = new TypeExtInfo(){Requirements = new List<Type>()};
                        foreach (var para in builderInfo?.GetParameters())
                        {
                            info.Requirements.Add(para.ParameterType);
                        }
                        info.Builder = (manager, scope) =>
                        {
                            object builder = null;
                            if (!builderInfo.IsStatic)
                                builder = Activator.CreateInstance(builderType);
                            List<object> para = new List<object>();
                            foreach (var req in info.Requirements)
                            {
                                var param = manager.GetService(req, scope);
                                if (param == null)
                                    throw new Exception($"Service not Found in scope {scope} : {req}");
                                para.Add(param);
                            }

                            return builderInfo.Invoke(builder, para.ToArray());
                        };
                        _serviceTypeInfo[depInfo] = info;
                    }
                    else
                    {
                        var ctorInfos = depInfo.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
                        var ctorInfo = ctorInfos.First();
                        if (ctorInfos.Length > 0)
                        {
                            Console.WriteLine($"Warning : Type {depInfo} has more than one Constructor.");
                        }

                        TypeExtInfo info = new TypeExtInfo() {Requirements = new List<Type>()};
                        foreach (var param in ctorInfo.GetParameters())
                        {
                            info.Requirements.Add(param.ParameterType);
                        }

                        info.Builder = (manager, scope) =>
                        {
                            List<object> para = new List<object>();
                            foreach (var req in info.Requirements)
                            {
                                var param = manager.GetService(req, scope);
                                if (param == null)
                                    throw new Exception($"Service not Found in scope {scope} : {req}");
                                para.Add(param);
                            }

                            return ctorInfo
                                .Invoke(para.ToArray());
                        };
                        _serviceTypeInfo[depInfo] = info;
                    }
                }
            }

        }

        #region NetworkInterface

        [Route("Status")]
        public string StatusData()
        {
            var status = _activeServices.Select(element =>
            {
                var srv = element.Instance;
                return new
                {
                    Alias = srv.Alias,
                    Type = srv.GetType().Name,
                    Status = ((srv as STKWorker)?.ServiceStatus)??true ? "Running" : "Crashed",
                    LastExecTime = ((srv as STKWorker)?.LastExecTime.ToString())??"-",
                    NextExecTime = ((srv as STKWorker)?.NextExecTime.ToString())??"-",
                };
            });
            /*, new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
            }*/
            var retContent = JsonConvert.SerializeObject(status,Formatting.Indented);
            Console.WriteLine(retContent);
            return retContent;
        }
        [Route("Status/{Alias}")]
        public object GetServiceStatus(string Alias)
        {
            var srv = _activeServices.FirstOrDefault(service => service.Instance.Alias == Alias)?.Instance;
            return new
            {
                Alias = srv?.Alias??"Not Found",
                Type = srv?.GetType()?.Name??"-",
                Status = ((srv as STKWorker)?.ServiceStatus) ?? true ? "Running" : "Crashed",
                LastExecTime = ((srv as STKWorker)?.LastExecTime.ToString()) ?? "-",
                NextExecTime = ((srv as STKWorker)?.NextExecTime.ToString()) ?? "-",
            };
        }

        [Route("Stop")]
        public string Terminate()
        {
            TerminateToken.Cancel();
            return "System Terminated.";
        }

        #endregion
    }

}
