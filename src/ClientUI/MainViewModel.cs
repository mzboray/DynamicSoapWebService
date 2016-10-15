using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ClientLib;
using ReactiveUI;

namespace ClientUI
{
    class MainViewModel : ReactiveObject
    {
        private WebServiceProxyCache _cache = new WebServiceProxyCache();
        private string _address;
        private WebEndpointInfo _selectedEndpoint;
        private MethodInfo _selectedMethod;

        public MainViewModel()
        {
            Parameters = new ReactiveList<ParameterViewModel>();
            Endpoints = new ReactiveList<WebEndpointInfo>();
            Methods = new ReactiveList<MethodInfo>();

            var canGet = this.WhenAny(x => x.Address, x =>
            {
                Uri r;
                return !string.IsNullOrWhiteSpace(x.Value) && Uri.TryCreate(x.Value, UriKind.Absolute, out r);
            });

            GetCommand = ReactiveCommand.CreateAsyncTask(canGet, o => Task.Run(() => _cache.GetProxyInfo(this.Address)));

            GetCommand.Subscribe(info =>
            {
                Endpoints.Clear();
                Endpoints.AddRange(info.Endpoints);
            });

            this.ObservableForProperty(x => x.SelectedEndpoint).Subscribe(x =>
            {
                Methods.Clear();

                if (x.Value == null)
                {
                    return;
                }

                foreach(var method in x.Value.Methods)
                {
                    Methods.Add(method);
                }
            });

            this.ObservableForProperty(x => x.SelectedMethod).Subscribe(x =>
            {
                Parameters.Clear();

                var method = x.Value;
                if (method == null)
                {
                    return;
                }

                foreach (var parameter in method.GetParameters())
                {
                    var flatParameters = GetFlattenedParameters(parameter);

                    foreach (var fp in flatParameters)
                    {
                        Parameters.Add(fp);
                    }
                }
            });
        }

        public void Initialize(StartupArgs args)
        {
            if (args.Address != null)
            {
                IDisposable sub = null;
                sub = GetCommand.Subscribe(info =>
                {
                    if (args.ServiceName != null)
                    {
                        SelectedEndpoint = Endpoints.FirstOrDefault(e => e.Name == args.ServiceName);
                    }

                    if (args.Method != null)
                    {
                        SelectedMethod = Methods.FirstOrDefault(m => m.Name == args.Method);
                    }

                    sub?.Dispose();
                });

                Address = args.Address;
                GetCommand.Execute(null);
            }
        }

        public ReactiveCommand<WebServiceInformation> GetCommand { get; set; }

        public string Address
        {
            get { return _address; }

            set
            {
                this.RaiseAndSetIfChanged(ref _address, value);
            }
        }

        public WebEndpointInfo SelectedEndpoint
        {
            get { return _selectedEndpoint; }

            set
            {
                this.RaiseAndSetIfChanged(ref _selectedEndpoint, value);
            }
        }

        public MethodInfo SelectedMethod
        {
            get { return _selectedMethod; }

            set
            {
                this.RaiseAndSetIfChanged(ref _selectedMethod, value);
            }
        }

        public ReactiveList<WebEndpointInfo> Endpoints { get; set; }

        public ReactiveList<ParameterViewModel> Parameters { get; set; }

        public ReactiveList<MethodInfo> Methods { get; set; }

        private List<ParameterViewModel> GetFlattenedParameters(ParameterInfo pInfo)
        {
            var p = new List<ParameterViewModel>();

            GetFlattenedParameters(p, pInfo.Name, pInfo.ParameterType);

            return p;
        }

        private void GetFlattenedParameters(List<ParameterViewModel> parameters, string nameBuilder, Type current)
        {
            if (IsSimpleType(current))
            {
                var vm = new ParameterViewModel() { Name = nameBuilder.ToString(), Type = current.FullName };
                parameters.Add(vm);
                return;
            }

            var properties = current.GetProperties().Where(p => p.GetSetMethod() != null).ToArray();
            if (properties.Length == 0)
            {
                var vm = new ParameterViewModel() { Name = nameBuilder.ToString(), Type = current.FullName };
                parameters.Add(vm);
            }
            else
            {
                foreach(var prop in properties)
                {
                    GetFlattenedParameters(parameters, nameBuilder + "." + prop.Name, prop.PropertyType);
                }
            }
        }

        private static bool IsSimpleType(Type type)
        {
            if (type == typeof(string) || type == typeof(char) || type == typeof(int) || type == typeof(double) 
                || type == typeof(Guid) || type == typeof(DateTime))
            {
                return true;
            }

            if (type.IsEnum)
            {
                return true;
            }

            return false;
        }
    }

    class ParameterViewModel : ReactiveObject
    {
        public string Name { get; set; }

        public string Type { get; set; }
    }

    class EndpointViewModel : ReactiveObject
    {
        public string Name { get; set; }
    }
}
