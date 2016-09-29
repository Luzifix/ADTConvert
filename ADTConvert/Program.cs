using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ADTConvert
{
    class Program
    {
        static void Main(string[] args)
        {
            bool silentMode = (args.Contains("-s") || args.Contains("--silent"));
            if (silentMode)
                Console.SetOut(TextWriter.Null);

            Console.WriteLine("--------------------------------");
            Console.WriteLine("ADTConverter");
            Console.WriteLine("Create by Luzifix");
            Console.WriteLine("E-Mail: luzifix@schattenhain.de");
            Console.WriteLine("--------------------------------");

            if (args.Length <= 0)
            {
                ConsoleErrorEnd();
            }

            string filename = Path.GetFullPath(args[0]);
            bool verbose = (args.Contains("-v") || args.Contains("--verbose"));

            if (!File.Exists(filename))
            {
                ConsoleErrorEnd($"File {filename} not found!");
            }

            
            if (!args.Contains("-noUpdate") && !args.Contains("--disableUpdateCheck"))
                VersionCheck.CheckForUpdate(verbose);

            new Main(args[0], verbose);

            if(!silentMode)
            {
                Console.WriteLine("\nPress any key to close the converter");
                Console.ReadKey();
            }
        }

        public static void ConsoleErrorEnd(string error = "")
        {
            Console.WriteLine(" ");
            string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            if (error.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error");
                Console.Error.WriteLine("  {0}\n", error);
                Console.ResetColor();
            }

            Console.WriteLine("Help");
            Console.WriteLine("  {0}.exe filename [-v]\n", processName);
            Console.WriteLine("Parameter");
            Console.WriteLine("  -v, --verbose\t\t\t\tPrints all messages to standard output");
            Console.WriteLine("  -s, --silent\t\t\t\tDisable all messages");
            Console.WriteLine("  -noUpdate, --disableUpdateCheck\tDisable the Update check");

            Console.ReadKey();
            Environment.Exit(0);
        }
    }
}
