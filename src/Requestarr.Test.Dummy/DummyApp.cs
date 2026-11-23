using System;
using System.Diagnostics;

namespace Requestarr.Test.Dummy
{
    public class DummyApp
    {
        public const string DUMMY_PROCCESS_NAME = "Requestarr.Test.Dummy";

        private static void Main()
        {
            var process = Process.GetCurrentProcess();

            Console.WriteLine("Dummy process. ID:{0} Name:{1} Path:{2}", process.Id, process.ProcessName, process.MainModule.FileName);
            Console.ReadLine();
        }
    }
}
