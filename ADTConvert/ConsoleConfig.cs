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
        public bool Legion { get; set; }
        public float LegionBoundingBox { get; set; } = 300.0f;
        public bool NoTables { get; set; } = false;
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
            Legion = (args.Contains("-l") || args.Contains("--legion"));
            Help = (args.Contains("-h") || args.Contains("--help"));
            NoTables = (args.Contains("-nt") || args.Contains("--noTables"));

            Input = Path.GetFullPath(args[0]);

            IEnumerable<string> outPath;
            IEnumerable<string> boundingBox;

            outPath = args.Where(s => s.StartsWith("-o=") || s.StartsWith("--output="));
            if (outPath.Count() == 1)
            {
                Output = Path.GetFullPath(outPath.First().Split('=').Last());
            }

            boundingBox = args.Where(s => s.StartsWith("-lb=") || s.StartsWith("--legionBoundingBox"));
            if (boundingBox.Count() == 1)
            {
                LegionBoundingBox = Single.Parse(boundingBox.First().Split('=').Last());
            }

            return this;
        }
    }
}
