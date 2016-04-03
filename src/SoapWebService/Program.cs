using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;
using NLog.Config;

namespace SoapWebService
{
    class Program
    {
        static void Main(string[] args)
        {
            SimpleConfigurator.ConfigureForConsoleLogging(LogLevel.Trace);
            var service = new WebService();
            string hostName = Dns.GetHostEntry("").HostName;
            Uri uri = new Uri($"http://{hostName}:8095/test");
            using (ServiceHost host = new ServiceHost(service))
            {
                var binding = new BasicHttpBinding();
                var ep = host.AddServiceEndpoint(typeof(IWebService), binding, uri);
                ep.Name = "ServiceName1";
                var ep2 = host.AddServiceEndpoint(typeof(IWebService2), binding, uri);
                ep2.Name = "ServiceName2";
                var smb = new ServiceMetadataBehavior() { HttpGetEnabled = true, HttpGetUrl = uri };
                host.Description.Behaviors.Add(smb);

                // This only works for a single endpoint.
                // host.Description.Name = "HostServiceName";

                host.Open();
                Console.WriteLine("Running at {0}", uri);
                Console.ReadLine();
            }
        }
    }

    [XmlSerializerFormat]
    [ServiceContract]
    public interface IWebService
    {
        [OperationContract]
        void SimpleInt32(int i);

        [OperationContract]
        void SimpleString(string s);

        [OperationContract]
        void ComplexObject(string s, MyClass c);

        [OperationContract]
        void ComplexObject2(MyClass2 c, DateTime d, Guid g);
    }

    [XmlSerializerFormat]
    [ServiceContract]
    public interface IWebService2
    {
        [OperationContract]
        void SomeMethod(int i);
    }

    [DataContract]
    public class MyClass
    {
        [DataMember]
        public int I { get; set; }

        [DataMember]
        public double J { get; set; }
    }

    [DataContract]
    public class MyClass2
    {
        [DataMember]
        public string A { get; set; }

        [DataMember]
        public Test B { get; set; }

        [DataMember]
        public MyClass C { get; set; }
    }

    public enum Test
    {
        One, Two, Three
    }

    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class WebService : IWebService, IWebService2
    {
        private static readonly Logger _Logger = LogManager.GetCurrentClassLogger();
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            Converters =
            {
                new StringEnumConverter()
            }
        };

        public void SimpleInt32(int i)
        {
            LogMethod(new { i });
        }

        public void SimpleString(string s)
        {
            LogMethod(new { s });
        }

        public void ComplexObject(string s, MyClass c)
        {
            LogMethod(new { s, c });
        }

        public void ComplexObject2(MyClass2 c, DateTime d, Guid g)
        {
            LogMethod(new { c, d, g });
        }

        private static void LogMethod<T>(T parameters, [CallerMemberName]string methodName = null)
        {
            string json = JsonConvert.SerializeObject(parameters, Settings);
            _Logger.Info("Called {0}: {1}", methodName, json);
        }

        public void SomeMethod(int i)
        {
            LogMethod(new { i });
        }
    }
}
