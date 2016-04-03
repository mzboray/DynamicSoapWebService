﻿using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Services;
using System.Web.Services.Description;
using System.Web.Services.Discovery;
using System.Web.Services.Protocols;
using System.Xml.Serialization;
using Microsoft.CSharp;
using NLog;

namespace SoapWebServiceClient
{
    class WebServiceProxyCache
    {
        private static readonly Logger _Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, Tuple<Assembly, WebServiceInformation>> _cache = new Dictionary<string, Tuple<Assembly, WebServiceInformation>>();

        public WebServiceProxyCache()
        {
            Namespace = "WebService.AutogeneratedTypes";
        }

        public string Namespace { get; set; }

        /// <summary>
        /// Emit additional debug information on code generation and compilation.
        /// </summary>
        public bool Debug { get; set; }

        /// <summary>
        /// Include Task-based async methods.
        /// </summary>
        public bool WithAsync { get; set; }

        public WebServiceInformation GetProxyInfo(Uri uri)
        {
            return GetProxyInfo(uri.AbsoluteUri);
        }

        public WebServiceInformation GetProxyInfo(string uri)
        {
            Tuple<Assembly, WebServiceInformation> info;
            if (_cache.TryGetValue(uri, out info))
            {
                return info.Item2;
            }

            var asm = GenerateWebServiceProxyAssembly(new Uri(uri), Namespace, WithAsync, Debug);
            var serviceInfo = Create(uri, asm);
            _cache[uri] = Tuple.Create(asm, serviceInfo);

            return serviceInfo;
        }

        public object GetServiceProxyObject(Uri uri, string name)
        {
            return GetServiceProxyObject(uri.AbsoluteUri, name);
        }

        public object GetServiceProxyObject(Uri uri)
        {
            return GetServiceProxyObject(uri.AbsoluteUri);
        }

        public object GetServiceProxyObject(string uri, string name)
        {
            var info = GetProxyInfo(uri);
            var endpoint = info.Endpoints.First(i => i.Name == name);
            object serviceObject = Activator.CreateInstance(endpoint.ProxyType);
            return serviceObject;
        }

        public object GetServiceProxyObject(string uri)
        {
            var info = GetProxyInfo(uri);
            var endpoint = info.Endpoints.First();
            object serviceObject = Activator.CreateInstance(endpoint.ProxyType);
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

        private Assembly GenerateWebServiceProxyAssembly(Uri uri, string @namespace, bool withAsync, bool debug)
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
            string codeHash;
            if (debug)
            {
                _Logger.Trace("Generated code: {0}", code);
                codeHash = ComputeHash(code);
                compilerResults = cSharpCodeProvider.CompileAssemblyFromSource(compilerParameters, code);
            }
            else
            {
                codeHash = ComputeHash(cSharpCodeProvider, codeCompileUnit);
                compilerResults = cSharpCodeProvider.CompileAssemblyFromDom(compilerParameters, codeCompileUnit);
            }

            _Logger.Info("Uri: {0}, Code hash: {1}", uri, codeHash);

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

        private static WebServiceInformation Create(string uri, Assembly proxyAssembly)
        {
            var serviceTypes = new List<Type>();
            foreach(var type in proxyAssembly.GetTypes())
            {
                if (Attribute.IsDefined(type, typeof(WebServiceBindingAttribute), false))
                {
                    serviceTypes.Add(type);
                }
            }

            return new WebServiceInformation(uri, serviceTypes.Select(t => new WebEndpointInfo(t)));
        }
    }

    class WebServiceInformation
    {
        public WebServiceInformation(string uri, IEnumerable<WebEndpointInfo> endpoints)
        {
            Uri = uri;
            Endpoints = endpoints.ToArray();
        }

        public string Uri { get; }

        public IReadOnlyList<WebEndpointInfo> Endpoints { get; }
    }

    class WebEndpointInfo
    {
        public WebEndpointInfo(Type proxyType)
        {
            Name = proxyType.Name;
            ProxyType = proxyType;
            Methods = proxyType.GetMethods().Where(mi => IsWebServiceMethod(mi)).ToArray();
        }

        public string Name { get; }

        public Type ProxyType { get; }

        public IReadOnlyList<MethodInfo> Methods { get; }

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