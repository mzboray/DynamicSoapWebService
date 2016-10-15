using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ClientUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var startupArgs = new StartupArgs();
            if (e.Args.Length > 0)
            {
                startupArgs.Address = e.Args[0];
            }

            if (e.Args.Length > 1)
            {
                startupArgs.ServiceName = e.Args[1];
            }

            if (e.Args.Length > 2)
            {
                startupArgs.Method = e.Args[2];
            }

            var vm = new MainViewModel();

            MainWindow = new MainWindow()
            {
                DataContext = vm
            };

            vm.Initialize(startupArgs);

            MainWindow.Show();
        }
    }

    class StartupArgs
    {
        public string Address { get; set; }

        public string ServiceName { get; set; }

        public string Method { get; set; }
    }
}
