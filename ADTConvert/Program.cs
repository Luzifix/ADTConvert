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
            ConsoleConfig config = ConsoleConfig.Instance.ReadArgs(args);

            if (config.SilentMode)
                Console.SetOut(TextWriter.Null);

            Console.WriteLine("--------------------------------");
            Console.WriteLine("ADTConverter");
            Console.WriteLine("Create by Luzifix");
            Console.WriteLine("E-Mail: luzifix@schattenhain.de");
            Console.WriteLine("--------------------------------");

            if (!config.NoUpdate)
                VersionCheck.CheckForUpdate();
            
            if(config.Help)
            {
                ConsoleErrorEnd();
            }

            // Check if input file/dir exist
            if (!File.Exists(config.Input) && !Directory.Exists(config.Input) && !config.Watch)
            {
                ConsoleErrorEnd($"Input File or Directory {config.Input} not found!");
            }
            else if(!Directory.Exists(config.Input) && config.Watch)
            {
                ConsoleErrorEnd($"Directory {config.Input} not found!");
            }
            
            // Check if output dir set and exist
            if(config.Output != null && !Directory.Exists(config.Output))
            {
                ConsoleErrorEnd($"Output directory {config.Output} not found!");
            }

            new Main();

            if(!config.SilentMode && !config.Watch)
            {
                Console.WriteLine("\nPress ESC to close the converter");
                while (Console.ReadKey().Key != ConsoleKey.Escape) { }
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
            Console.WriteLine("  {0}.exe input [parameter]\n", processName);
            Console.WriteLine("Parameter");
            Console.WriteLine("  -o, --output\t\t\t\tOutput path");
            Console.WriteLine("  -v, --verbose\t\t\t\tPrints all messages to standard output");
            Console.WriteLine("  -s, --silent\t\t\t\tDisable all messages");
            Console.WriteLine("  -w, --watch\t\t\t\tStart watch mode (Beta)");
            Console.WriteLine("  -h, --help\t\t\t\tShow Help");

            Console.WriteLine("  -noUpdate, --disableUpdateCheck\tDisable the Update check");

            Console.WriteLine("\nPress ESC to close the converter");
            while (Console.ReadKey().Key != ConsoleKey.Escape) { }
            Environment.Exit(0);
        }
    }
}
