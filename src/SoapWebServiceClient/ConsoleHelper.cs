using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SoapWebServiceClient
{
    class ConsoleHelper
    {
        /// <summary>
        /// Write "Press any key to continue" only if the console is owned by the current process.
        /// http://stackoverflow.com/a/13256385
        /// </summary>
        public static void PressAnyKey()
        {
            if (GetConsoleProcessList(new int[2], 2) <= 1)
            {
                Console.Write("Press any key to continue");
                Console.ReadKey();
            }
        }

        [DllImport("kernel32.dll")]
        private static extern int GetConsoleProcessList(int[] buffer, int size);
    }
}
