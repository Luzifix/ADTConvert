using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ADTConvert
{
    class ConsoleConfig
    {
        private static ConsoleConfig instance;

        public string Input { get; set; }
        public string Output { get; set; }
        public bool SilentMode { get; set; }
        public bool Verbose { get; set; }
        public bool NoUpdate { get; set; }
        public bool Watch { get; set; }
        public bool Help { get; set; }

        private ConsoleConfig() { }

        public static ConsoleConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ConsoleConfig();
                }
                return instance;
            }
        }

        public ConsoleConfig ReadArgs(string[] args)
        {
            if (args.Length <= 0)
                return this;

            SilentMode = (args.Contains("-s") || args.Contains("--silent"));
            Verbose = (args.Contains("-v") || args.Contains("--verbose"));
            NoUpdate = (args.Contains("-noUpdate") || args.Contains("--disableUpdateCheck"));
            Watch = (args.Contains("-w") || args.Contains("--watch"));
            Help = (args.Contains("-h") || args.Contains("--help"));

            Input = Path.GetFullPath(args[0]);

            IEnumerable<string> outPath;

            outPath = args.Where(s => s.StartsWith("-o="));
            if (outPath.Count() == 1)
            {
                Output = Path.GetFullPath(outPath.First().Split('=').Last());
            }

            outPath = args.Where(s => s.StartsWith("--output="));
            if (Output == null && outPath.Count() == 1)
            {
                Output = Path.GetFullPath(outPath.First().Split('=').Last());
            }

            return this;
        }
    }
}
