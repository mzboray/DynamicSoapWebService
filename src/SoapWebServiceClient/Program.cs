using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Services.Protocols;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace SoapWebServiceClient
{
    class Program
    {
        private static readonly Logger _Logger = LogManager.GetLogger("SoapWebServiceClient");

        static void Main(string[] args)
        {
            try
            {
                var console = new ConsoleTarget();
                console.Layout = "${longdate}|${level:uppercase=true}|${logger}|${trim-whitespace:${message} ${exception:format=tostring}}";
                SimpleConfigurator.ConfigureForTargetLogging(console, LogLevel.Trace);
                var proxyCache = new WebServiceProxyCache() { Debug = false };

                while (true)
                {
                    Console.Write("Enter a web service url: ");
                    string input = Console.ReadLine();
                    Uri uri;
                    if (string.IsNullOrEmpty(input))
                    {
                        string hostName = Dns.GetHostEntry("").HostName;
                        uri = new Uri($"http://{hostName}:8095/test");
                    }
                    else
                    {
                        uri = new Uri(input);
                    }

                    object serviceObject = proxyCache.GetServiceProxyObject(uri);

                    var type = serviceObject.GetType();
                    Console.WriteLine($"Service name: {type.Name}");

                    Console.WriteLine("Methods:");
                    foreach (var method in type.GetMethods())
                    {
                        bool isWebServiceMethod = IsWebServiceMethod(method);
                        if (!isWebServiceMethod)
                            continue;

                        Console.WriteLine(method);
                    }

                    Console.Write("Enter method: ");
                    string methodName = Console.ReadLine();

                    var methodToInvoke = type.GetMethod(methodName);

                    var parameters = new List<object>();
                    foreach (var parameterInfo in methodToInvoke.GetParameters())
                    {
                        object p = ReadValueOfType(parameterInfo.Name, parameterInfo.ParameterType);
                        parameters.Add(p);
                    }

                    object output = methodToInvoke.Invoke(serviceObject, parameters.ToArray());
                    if (methodToInvoke.ReturnType != typeof(void))
                        Console.WriteLine("Output: {0}", output);
                }
            }
            catch (Exception e)
            {
                _Logger.Error(e, "An error was encountered");
            }
        }

        private static object ReadValueOfType(string name, Type type)
        {
            if (type.IsClass && type != typeof(string))
            {
                var p = Activator.CreateInstance(type);
                foreach (var prop in p.GetType().GetProperties())
                {
                    object value = ReadValueOfType(prop.Name, prop.PropertyType);
                    prop.SetValue(p, value);
                }

                return p;
            }
            else
            {
                Console.Write("Enter a value for {0} {1}: ", name, type);
                string input = Console.ReadLine();
                object value;
                MethodInfo parseMethod;
                try
                {
                    if (type.IsEnum)
                    {
                        value = Enum.Parse(type, input, true);
                    }
                    else if (typeof(IConvertible).IsAssignableFrom(type))
                    {
                        value = Convert.ChangeType(input, type);
                    }
                    else if (TryGetParseMethod(type, out parseMethod))
                    {
                        value = parseMethod.Invoke(null, new object[] { input });
                    }
                    else
                    {
                        Console.WriteLine("Can't handle type: {0}", type);
                        value = null;
                    }
                }
                catch
                {
                    value = null;
                }

                return value;
            }
        }

        private static bool TryGetParseMethod(Type type, out MethodInfo parseMethod)
        {
            parseMethod = type.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
            return parseMethod != null;
        }

        private static bool IsWebServiceMethod(MethodInfo method)
        {
            foreach (Attribute attr in method.GetCustomAttributes(false))
            {
                if (attr is SoapDocumentMethodAttribute || attr is SoapRpcMethodAttribute)
                {
                    return true;
                }
            }

            return false;
        }
    }
}