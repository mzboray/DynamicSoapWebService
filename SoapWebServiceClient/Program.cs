using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Services;
using System.Web.Services.Description;
using System.Web.Services.Discovery;
using System.Web.Services.Protocols;
using System.Xml.Serialization;
using Microsoft.CSharp;
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
                var proxyCache = new WebServiceProxyCache() { Debug = true };

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
                        //_Logger.Info("Method: {0}", method.Name);
                        //foreach (var parameter in method.GetParameters())
                        //{
                        //    var sb = new StringBuilder();
                        //    sb.AppendFormat("Parameter {0} {1}", parameter.Name, parameter.ParameterType).AppendLine();
                        //    WriteProperties(parameter.ParameterType, sb);
                        //    _Logger.Info(sb);
                        //}
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

        private static void WriteProperties(Type type, StringBuilder sb, int indent = 0)
        {
            if (type.IsPrimitive ||
                type == typeof(string) ||
                type == typeof(DateTime) ||
                type == typeof(Guid) ||
                type == typeof(object))
            {
                return;
            }

            foreach (var prop in type.GetProperties())
            {
                if (prop.PropertyType.IsPrimitive ||
                    prop.PropertyType == typeof(string) ||
                    prop.PropertyType == typeof(DateTime) ||
                    prop.PropertyType == typeof(Guid))
                {
                    sb.AppendFormat("{0}{1} {2}", new string(' ', indent * 4), prop.Name, prop.PropertyType).AppendLine();
                }
                else if (indent < 3)
                {
                    WriteProperties(prop.PropertyType, sb, indent + 1);
                }
                else
                {
                    sb.AppendLine("{Reached recursive cutoff}");
                }
            }
        }
    }

    class WebServiceProxyCache
    {
        private static readonly Logger _Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, Assembly> _cache = new Dictionary<string, Assembly>();

        public WebServiceProxyCache()
        {
            Namespace = "Webservice.AutogeneratedTypes";
        }

        public string Namespace { get; set; }

        public bool Debug { get; set; }

        public Assembly GetProxyAssembly(Uri uri)
        {
            return GetProxyAssembly(uri.AbsoluteUri);
        }

        public Assembly GetProxyAssembly(string uri)
        {
            Assembly asm;
            if (_cache.TryGetValue(uri, out asm))
            {
                return asm;
            }

            asm = GenerateWebServiceProxyAssembly(new Uri(uri), Namespace, false, Debug);
            _cache[uri] = asm;

            return asm;
        }

        public object GetServiceProxyObject(Uri uri)
        {
            return GetServiceProxyObject(uri.AbsoluteUri);
        }

        public object GetServiceProxyObject(string uri)
        {
            var asm = GetProxyAssembly(uri);
            object serviceObject = InstantiateWebServiceProxy(asm);
            return serviceObject;
        }

        private static object InstantiateWebServiceProxy(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                object[] customAttributes = type.GetCustomAttributes(typeof(WebServiceBindingAttribute), false);
                if (customAttributes.Length > 0)
                {
                    object instance = assembly.CreateInstance(type.ToString());
                    return instance;
                }
            }

            throw new InvalidOperationException("Proxy type not found.");
        }

        private static Assembly GenerateWebServiceProxyAssembly(Uri uri, string @namespace, bool withAsync, bool debug)
        {
            var discoveryClientProtocol = new DiscoveryClientProtocol()
            {
                UseDefaultCredentials = true,
                AllowAutoRedirect = true,
            };
            discoveryClientProtocol.DiscoverAny(uri.ToString());
            discoveryClientProtocol.ResolveAll();

            var codeNamespace = new CodeNamespace();
            if (!string.IsNullOrEmpty(@namespace))
            {
                codeNamespace.Name = @namespace;
            }

            var webReferenceCollection = new WebReferenceCollection()
            {
                new WebReference(discoveryClientProtocol.Documents, codeNamespace)
            };

            var codeCompileUnit = new CodeCompileUnit()
            {
                Namespaces =
                {
                    codeNamespace
                }
            };

            var options = CodeGenerationOptions.GenerateProperties;
            if (withAsync)
            {
                options |= CodeGenerationOptions.GenerateNewAsync;
            }

            var webReferenceOptions = new WebReferenceOptions()
            {
                CodeGenerationOptions = options,
                Verbose = true
            };

            var cSharpCodeProvider = new CSharpCodeProvider();
            var webReferenceWarnings = ServiceDescriptionImporter.GenerateWebReferences(webReferenceCollection, cSharpCodeProvider, codeCompileUnit, webReferenceOptions);
            string code = null;
            if (debug)
            {
                var stringBuilder = new StringBuilder();
                using (var writer = new StringWriter(stringBuilder, CultureInfo.InvariantCulture))
                {
                    cSharpCodeProvider.GenerateCodeFromCompileUnit(codeCompileUnit, writer, null);
                    code = stringBuilder.ToString();
                }

                _Logger.Debug("Generated code: {0}", code);

                foreach (string warning in webReferenceWarnings)
                {
                    _Logger.Info("WebReference generation warning {0}", warning);
                }
            }

            var compilerParameters = new CompilerParameters()
            {
                ReferencedAssemblies =
                {
                    "System.dll",
                    "System.Data.dll",
                    "System.Xml.dll",
                    "System.Web.Services.dll",
                    Assembly.GetExecutingAssembly().Location
                },

                GenerateExecutable = false,
                GenerateInMemory = true,
                TreatWarningsAsErrors = false,
                WarningLevel = 4,
            };

            CompilerResults compilerResults;
            if (debug)
            {
                //_Logger.Trace("Generated code: {0}", code);
                compilerResults = cSharpCodeProvider.CompileAssemblyFromSource(compilerParameters, code);
            }
            else
            {
                string hash = ComputeHash(cSharpCodeProvider, codeCompileUnit);
                _Logger.Trace("Code hash: {0}", hash);
                compilerResults = cSharpCodeProvider.CompileAssemblyFromDom(compilerParameters, codeCompileUnit);
            }

            if (compilerResults.Errors.Count > 0)
            {
                _Logger.Error("Found {0} errors in generated code for uri {1}", compilerResults.Errors.Count, uri);
                foreach (CompilerError error in compilerResults.Errors)
                {
                    _Logger.Debug("Compiler error:{0} {1} {2}", error.ErrorNumber, error.ErrorText, error.Line);
                }
            }

            return compilerResults.CompiledAssembly;
        }

        /// <summary>
        /// Computes a SHA256 hash of the generated code stream. Useful for debugging.
        /// </summary>
        /// <param name="provider"></param>
        /// <param name="codeCompileUnit"></param>
        /// <returns></returns>
        private static string ComputeHash(CSharpCodeProvider provider, CodeCompileUnit codeCompileUnit)
        {
            using (SHA256 hashAlg = SHA256.Create())
            using (CryptoStream cs = new CryptoStream(Stream.Null, hashAlg, CryptoStreamMode.Write))
            using (StreamWriter writer = new StreamWriter(cs, Encoding.Unicode))
            {
                provider.GenerateCodeFromCompileUnit(codeCompileUnit, writer, null);
                cs.FlushFinalBlock();
                byte[] hash = hashAlg.Hash;
                return BitConverter.ToString(hash);
            }
        }

        /// <summary>
        /// Computes a SHA256 hash of the string.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static string ComputeHash(string input)
        {
            byte[] inputBytes = Encoding.Unicode.GetBytes(input);
            using (SHA256 hashAlg = SHA256.Create())
            {
                byte[] hash = hashAlg.ComputeHash(inputBytes);
                return BitConverter.ToString(hash);
            }
        }
    }
}