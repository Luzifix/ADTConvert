using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ADTConvert
{
    class Main
    {
        struct DataChunk
        {
            public string Signature;
            public int Size;
            public byte[] Data;
        }

        bool inputIsDir;
        BinaryReader inputReader = null;
        private string adtName = "";
        private string exportPath = "";
        List<DataChunk> chunks = new List<DataChunk>();
        ConsoleConfig config = ConsoleConfig.Instance;
        private HashSet<string> filesToProcess = new HashSet<string>();


        public Main()
        {
            inputIsDir = Directory.Exists(config.Input);
            if (inputIsDir && config.Watch)
            {
                FileSystemWatcher watcher = new FileSystemWatcher();
                watcher.Path = config.Input;
                watcher.NotifyFilter = NotifyFilters.LastWrite;
                watcher.Filter = "*.adt";
                watcher.Changed += new FileSystemEventHandler(OnADTChanged);
                watcher.EnableRaisingEvents = true;
                watcher.IncludeSubdirectories = true;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Watcher active");
                Console.ResetColor();

                Console.WriteLine("\nPress ESC to stop the watcher");
                while(Console.ReadKey().Key != ConsoleKey.Escape) { }

                watcher.Dispose();
            }
            else if(inputIsDir && !config.Watch)
            {
                foreach (string file in Directory.EnumerateFiles(config.Input, "*.adt", SearchOption.AllDirectories))
                {
                    convertADT(file);
                }
            }
            else
            {
                convertADT(config.Input);
            }
        }

        private void OnADTChanged(object sender, FileSystemEventArgs e)
        {
            if(filesToProcess.Contains(e.FullPath))
            {
                filesToProcess.Remove(e.FullPath);
                return;
            }

            Thread.Sleep(250);
            convertADT(e.FullPath);
            Clear();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Convert complete");
            Console.ResetColor();

            Console.WriteLine("\nPress ESC to stop the watcher");
            filesToProcess.Add(e.FullPath);
        }

        private bool convertADT(string input)
        {
            Console.WriteLine("\n--- Base ADT Load ---");
            adtName = Path.GetFileName(input);
            exportPath = (Path.GetDirectoryName(input) == "" ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(Path.GetDirectoryName(input))) + (config.Watch || inputIsDir ? "/.." : "") + "/export/";
            exportPath = (config.Output != null ? config.Output + "/" : exportPath);

            if(inputIsDir)
            {
                string folderStruct = input.Replace(config.Input, "");

                if (folderStruct != "")
                    exportPath += Path.GetDirectoryName(folderStruct) + "/";
            }

            if (!Directory.Exists(exportPath))
            {
                if (config.Verbose)
                    Console.WriteLine("Debug: Create export dir in {0}", exportPath);

                Directory.CreateDirectory(exportPath);
            }

            #region Open and check original ADT
            try
            {
                Console.WriteLine("Info: Open {0}", adtName);

                FileInfo fileInfo = new FileInfo(input);
                FileStream baseStream = fileInfo.OpenRead();
                inputReader = new BinaryReader(baseStream);
            }
            catch (Exception e)
            {
                Program.ConsoleErrorEnd(e.Message);
            }

           
            #endregion

            using (inputReader)
            {
                if (!Helper.SeekChunk(inputReader, "MVER") || !Helper.SeekChunk(inputReader, "MHDR") || !Helper.SeekChunk(inputReader, "MCIN"))
                {
                    Program.ConsoleErrorEnd("ADT file is corrupted");
                    return false;
                }

                #region Load all chunks
                Console.WriteLine("Info: Load all chunks");
                chunks.Clear();
                inputReader.BaseStream.Seek(0, SeekOrigin.Begin);
                while (inputReader.BaseStream.Position + 8 < inputReader.BaseStream.Length)
                {
                    string magic = Helper.Reverse(new string(inputReader.ReadChars(4)));
                    int size = inputReader.ReadInt32() + 8;
                    inputReader.BaseStream.Position -= 8;
                    byte[] data = inputReader.ReadBytes(size);

                    if (config.Verbose)
                        Console.WriteLine("Debug: Load {0}", magic);

                    chunks.Add(new DataChunk
                    {
                        Data = data,
                        Signature = magic,
                        Size = size
                    });
                }
                #endregion

                if (createRoot())
                    if (createTex())
                        if (createObj())
                        {
                            inputReader.Close();
                            return true;
                        }

                inputReader.Close();
            }
            
            return false;
        }

        private void Clear()
        {
            adtName = "";
            exportPath = "";
            chunks.Clear();

            if(inputReader != null)
            {
                inputReader.Close();
                inputReader = null;
            }
        }

        private bool createRoot()
        {
            Console.WriteLine("\n--- Root ADT Convert ---");
            BinaryReader rootReader = null;
            BinaryWriter rootWriter = null;
            List<string> rootChunks = new List<string> { "MVER" /*, /*"MHDR", "MH2O", "MCNK", "MFBO"*/ };
            List<string> mcnkSubChunks = new List<string> { "MCTV", "MCVT", "MCLV", "MCCV", "MCNR", "MCSE" };

            if (!createBase(ref rootReader, ref rootWriter, rootChunks, ".adt"))
                return false;

            #region Write empty MHDR chunk
            Console.WriteLine("Info: Create MHDR chunk");
            writeChunk(rootWriter, "MHDR", 64, new byte[64]);
            #endregion

            #region Copy & clean MH2O chunk
            if (Helper.SeekChunk(inputReader, "MH2O"))
            {
                try
                {
                    int size = rootReader.ReadInt32();
                    inputReader.BaseStream.Position -= sizeof(UInt32) * 2;  // magic && header size
                    size += sizeof(UInt32) * 2;                             // magic && header size
                    rootWriter.Write(inputReader.ReadBytes(size));
                }
                catch (System.IO.EndOfStreamException /*ex*/)
                {
                    // Water created with Allwater tool and is buggy ;)
                    Console.WriteLine("Info: Fix MH2O chunk");

                    long mh2oStart = inputReader.BaseStream.Position += sizeof(UInt32);

                    List<string> searchChunks = new List<string> { "MVER", "MHDR", "MFBO", "MCNK" };
                    Helper.SeekChunkFormList(inputReader, searchChunks, false);
                    long mh2oEnd = inputReader.BaseStream.Position;
                    Int32 mh2oSize = (Int32)mh2oEnd - (Int32)mh2oStart;

                    inputReader.BaseStream.Position = mh2oStart;

                    writeChunk(rootWriter, "MH2O", mh2oSize, inputReader.ReadBytes(mh2oSize));
                }
            }
            #endregion

            #region Copy & clean MCNK
            Console.WriteLine("Info: Fix MCNK chunk for root");
            inputReader.BaseStream.Seek(0, SeekOrigin.Begin);
            rootWriter.BaseStream.Seek(0, SeekOrigin.End);
            while (Helper.SeekChunk(inputReader, "MCNK", false))
            {
                long chunkStart = inputReader.BaseStream.Position - sizeof(UInt32);
                uint size = inputReader.ReadUInt32();
                uint newSize = 0;
                long chunkEnd = chunkStart + sizeof(UInt32) * 2 + size;

                if (size > 0)
                {
                    inputReader.BaseStream.Position = chunkStart;
                    rootWriter.Write(inputReader.ReadBytes(8));                 // magic && old size


                        rootWriter.Write(inputReader.ReadUInt32());                 // flags
                        rootWriter.Write(inputReader.ReadUInt32());                 // IndexX
                        rootWriter.Write(inputReader.ReadUInt32());                 // IndexY

                        // nLayers
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // nDoodadRefs
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // ofsHeight
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // ofsNormal
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // ofsLayer
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // ofsRefs
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // ofsAlpha
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // sizeAlpha
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // ofsShadow
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        // sizeShadow
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        rootWriter.Write(inputReader.ReadUInt32());                 // areaid

                        // nMapObjRefs
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                        rootWriter.Write(inputReader.ReadUInt16());                 // holes_low_res
                        rootWriter.Write(inputReader.ReadUInt16());                 // unknown_but_used;
                        rootWriter.Write(inputReader.ReadBytes(16));                // ReallyLowQualityTextureingMap
                        rootWriter.Write(inputReader.ReadUInt32());                 // predTex
                        rootWriter.Write(inputReader.ReadUInt32());                 // noEffectDoodad
                        rootWriter.Write(inputReader.ReadUInt32());                 // ofsSndEmitters
                        rootWriter.Write(inputReader.ReadUInt32());                 // nSndEmitters
                        rootWriter.Write(inputReader.ReadUInt32());                 // ofsLiquid
                        rootWriter.Write(inputReader.ReadUInt32());                 // sizeLiquid
                        rootWriter.Write(inputReader.ReadBytes(sizeof(float) * 3)); // position
                        rootWriter.Write(inputReader.ReadUInt32());                 // ofsMCCV
                        rootWriter.Write(inputReader.ReadUInt32());                 // ofsMCLV

                        // unused
                        inputReader.BaseStream.Position += 4;
                        rootWriter.Write((UInt32)0);

                    newSize += 128;                                                 // header size

                    #region Read & write sub chunks
                    long afterHeaderPosition = inputReader.BaseStream.Position;
                    foreach (string subChunkName in mcnkSubChunks)
                    {
                        inputReader.BaseStream.Position = afterHeaderPosition;

                        #region Write MCNR sub chunk
                        if(subChunkName == "MCNR")
                        {
                            if (config.Verbose)
                                Console.WriteLine("Debug: Write MCNR chunk");

                            while (Helper.SeekSubChunk(inputReader, "MCNR", false, chunkEnd))
                            {
                                inputReader.BaseStream.Position += 4;                // old size

                                int mcnrSize = 448;
                                writeChunk(rootWriter, "MCNR", mcnrSize, inputReader.ReadBytes(mcnrSize));
                                newSize += sizeof(UInt32) * 2 + (uint)mcnrSize;
                            }
                            continue;
                        }
                        #endregion

                        if (Helper.SeekSubChunk(inputReader, subChunkName, false, chunkEnd))
                        {
                            int subChunkSize = inputReader.ReadInt32() + 8;
                            inputReader.BaseStream.Position -= 8;
                            byte[] subChunkData = inputReader.ReadBytes(subChunkSize);


                            rootWriter.Write(subChunkData);
                            newSize += (uint)subChunkSize;
                        }
                    }

                    #region Write new MCNK size
                    rootWriter.BaseStream.Position -= newSize + sizeof(UInt32);
                    rootWriter.Write(newSize); // new size
                    rootWriter.BaseStream.Seek(0, SeekOrigin.End);
                    #endregion
                    inputReader.BaseStream.Position = chunkEnd;
                    #endregion
                }
            }
            #endregion

            #region Write MFBO chunk if exist
            if (Helper.SeekChunk(inputReader, "MFBO"))
            {
                Console.WriteLine("Info: Copy MFBO chunk");
                rootWriter.BaseStream.Seek(0, SeekOrigin.End);
                int size = inputReader.ReadInt32();
                writeChunk(rootWriter, "MFBO", size, inputReader.ReadBytes(size));
            }
            else
            {
                
                Console.WriteLine("Info: Create MFBO chunk");
                rootWriter.BaseStream.Seek(0, SeekOrigin.End);

                string mfboData = "840384038403840384038403840384038403" + // max 900
                                  "6AFF6AFF6AFF6AFF6AFF6AFF6AFF6AFF6AFF";  // min -150

                byte[] data = Helper.ConvertHexStringToByteArray(mfboData);

                writeChunk(rootWriter, "MFBO", data.Length, data);

            }
            #endregion

            #region Set MHDR data
            if (Helper.SeekChunk(rootReader, "MHDR"))
            {
                Console.WriteLine("Info: Set MHDR data");
                long mhdrStart = rootReader.BaseStream.Position + 4;

                Helper.SeekChunk(rootReader, "MH2O");
                UInt32 mh2oOffset = (rootReader.BaseStream.Position >= rootReader.BaseStream.Length ? 0 : (UInt32)rootReader.BaseStream.Position - (UInt32)mhdrStart - sizeof(UInt32));

                Helper.SeekChunk(rootReader, "MFBO");
                UInt32 mfboOffset = (rootReader.BaseStream.Position >= rootReader.BaseStream.Length ? 0 : (UInt32)rootReader.BaseStream.Position - (UInt32)mhdrStart - sizeof(UInt32));

                rootWriter.BaseStream.Position = mhdrStart;
                rootWriter.Write((mfboOffset > 0 ? 1 : (UInt32)0));          // flags
                rootWriter.BaseStream.Position += sizeof(UInt32) * 8;        // skip mcin, mtex, mmdx, mmid, mwmo, mddf, modf
                rootWriter.Write(mfboOffset);
                rootWriter.Write(mh2oOffset);
            }
            #endregion

            rootWriter.Close();
            rootReader.Close();
            return true;
        }

        private bool createTex()
        {
            Console.WriteLine("\n--- Tex ADT Convert ---");
            string ext0 = "_tex0.adt";
            string ext1 = "_tex1.adt";
            BinaryReader texReader = null;
            BinaryWriter texWriter = null;
            List<string> texChunks = new List<string> { "MVER", "MAMP", "MTEX", /*"MCNK"*/ };
            List<string> mcnkSubChunks = new List<string> { "MCLY", "MCSH", "MCAL", "MCMT"};


            if (!createBase(ref texReader, ref texWriter, texChunks, ext0))
                return false;

            #region if MAMP not exist create a MAMP chunk
            if (!Helper.SeekChunk(inputReader, "MAMP", true))
            {
                Console.WriteLine("Info: Create MAMP chunk");
                byte[] data = { 0, 0, 0, 0 };
                writeChunk(texWriter, "MAMP", 4, data);
            }
            #endregion

            #region Copy & clean MCNK
            Console.WriteLine("Info: Fix MCNK chunk for tex");
            inputReader.BaseStream.Position = 0;
            while (Helper.SeekChunk(inputReader, "MCNK", false))
            {
                long chunkStart = inputReader.BaseStream.Position - sizeof(UInt32);
                uint size = inputReader.ReadUInt32();
                uint newSize = 0;
                long chunkEnd = chunkStart + sizeof(UInt32) * 2 + size;

                if (size > 0)
                {                    
                    inputReader.BaseStream.Position = chunkStart;
                    texWriter.Write(inputReader.ReadBytes(8)); // magic && old size
                    inputReader.BaseStream.Position += 128;    // skip header (128 bytes)

                    #region Read & writ sub chunks
                    long currentPosition = inputReader.BaseStream.Position;
                    foreach (string subChunkName in mcnkSubChunks)
                    {
                        inputReader.BaseStream.Position = currentPosition;
                        if (Helper.SeekSubChunk(inputReader, subChunkName, false, chunkEnd))
                        {
                            int subChunkSize = inputReader.ReadInt32() + 8;
                            inputReader.BaseStream.Position -= 8;
                            byte[] subChunkData = inputReader.ReadBytes(subChunkSize);


                            newSize += (uint)subChunkSize;
                            texWriter.Write(subChunkData);
                        }
                    }

                    #region if MCMT not exist create a MCMT sub chunk
                    inputReader.BaseStream.Position = currentPosition;
                    if (!Helper.SeekSubChunk(inputReader, "MCMT", false, chunkEnd))
                    {
                        if (config.Verbose)
                            Console.WriteLine("Info: Create MCMT subchunk");
                        newSize += 12;

                        byte[] data = { 0, 0, 0, 0 };
                        writeChunk(texWriter, "MCMT", 4, data);
                    }
                    #endregion
                    #endregion

                    #region Write new MCNK size
                    texWriter.BaseStream.Position -= newSize + sizeof(UInt32);
                    texWriter.Write(newSize); // new size
                    texWriter.BaseStream.Seek(0, SeekOrigin.End);
                    #endregion

                    inputReader.BaseStream.Position = chunkEnd;
                }
            }
            #endregion

            texWriter.Close();
            texReader.Close();
        
            #region Copy tex0 to tex1
            Console.WriteLine("Info: Copy tex0 to tex1");
            string basePath = exportPath + Path.GetFileNameWithoutExtension(adtName);

            File.Delete(basePath + ext1);
            File.Copy(basePath + ext0, basePath + ext1);
            #endregion

            return true;
        }

        private bool createObj()
        {
            Console.WriteLine("\n--- Obj ADT Convert ---");
            string ext0 = "_obj0.adt";
            string ext1 = "_obj1.adt";
            BinaryReader objReader = null;
            BinaryWriter objWriter = null;
            List<string> objChunks = new List<string> { "MVER", "MMDX", "MMID", "MWMO", "MWID", "MDDF", "MODF", /*"MCNK"*/ };
            List<string> mcnkSubChunks = new List<string> { "MCRD", "MCRW" };

            if (!createBase(ref objReader, ref objWriter, objChunks, ext0))
                return false;

            #region Copy & clean MCNK
            Console.WriteLine("Info: Fix MCNK chunk for obj");
            inputReader.BaseStream.Seek(0, SeekOrigin.Begin);
            while (Helper.SeekChunk(inputReader, "MCNK", false))
            {
                long chunkStart = inputReader.BaseStream.Position - sizeof(UInt32);
                uint size = inputReader.ReadUInt32();
                long chunkEnd = chunkStart + sizeof(UInt32) * 2 + size;

                uint newSize = 0;
                Int32 nDoodadRefs = 0;
                Int32 nMapObjRefs = 0;

                if (size > 0)
                {
                    inputReader.BaseStream.Position += 16;
                    nDoodadRefs = inputReader.ReadInt32();
                    inputReader.BaseStream.Position += 36;
                    nMapObjRefs = inputReader.ReadInt32();

                    inputReader.BaseStream.Position = chunkStart;
                    objWriter.Write(inputReader.ReadBytes(8)); // magic && old size
                    inputReader.BaseStream.Position += 128;    // skip header (128 bytes)

                    #region Read & writ sub chunks
                    long currentPosition = inputReader.BaseStream.Position;
                    foreach (string subChunkName in mcnkSubChunks)
                    {
                        inputReader.BaseStream.Position = currentPosition;
                        if (Helper.SeekSubChunk(inputReader, subChunkName, false, chunkEnd))
                        {
                            int subChunkSize = inputReader.ReadInt32() + 8;
                            inputReader.BaseStream.Position -= 8;
                            byte[] subChunkData = inputReader.ReadBytes(subChunkSize);


                            newSize += (uint)subChunkSize;
                            objWriter.Write(subChunkData);
                        }
                    }

                    #region Split MCRF in MCRD & MCRW
                    inputReader.BaseStream.Position = currentPosition;
                    if (Helper.SeekSubChunk(inputReader, "MCRF", false, chunkEnd))
                    {
                        if (config.Verbose)
                            Console.WriteLine("Info: Read MCRF subchunk");

                        inputReader.BaseStream.Position += sizeof(UInt32); // size

                        int mcrdSize = sizeof(UInt32) * nDoodadRefs;
                        int mcrwSize = sizeof(UInt32) * nMapObjRefs;

                        byte[] mcrdData = inputReader.ReadBytes(mcrdSize);
                        byte[] mcrwData = inputReader.ReadBytes(mcrwSize);

                        if (config.Verbose)
                            Console.WriteLine("Info: Create MCRD subchunk");
                        newSize += 8 + (uint)mcrdSize;

                        writeChunk(objWriter, "MCRD", mcrdSize, mcrdData);

                        if (config.Verbose)
                            Console.WriteLine("Info: Create MCRW subchunk");
                        newSize += 8 + (uint)mcrwSize;

                        writeChunk(objWriter, "MCRW", mcrwSize, mcrwData);
                    }
                    #endregion
                    #endregion

                    #region Write new MCNK size
                    objWriter.BaseStream.Position -= newSize + sizeof(UInt32);
                    objWriter.Write(newSize); // new size
                    objWriter.BaseStream.Seek(0, SeekOrigin.End);
                    #endregion

                    inputReader.BaseStream.Position = chunkEnd;
                }
            }
            #endregion

            objWriter.Close();
            objReader.Close();

            #region Copy obj0 to obj1
            Console.WriteLine("Info: Copy obj0 to obj1");
            string basePath = exportPath + Path.GetFileNameWithoutExtension(adtName);

            File.Delete(basePath + ext1);
            File.Copy(basePath + ext0, basePath + ext1);
            #endregion

            return true;
        }

        private bool createBase(ref BinaryReader reader, ref BinaryWriter writer, List<string> allowdChunks, string ext)
        {
            string baseName = Path.GetFileNameWithoutExtension(adtName) + ext;
            string baseADT = exportPath + baseName;

            #region Remove adt
            if (File.Exists(baseADT))
            {
                if (config.Verbose)
                    Console.WriteLine("Debug: Remove {0}", baseName);

                File.Delete(baseADT);
            }
            #endregion

            #region Create adt
            Console.WriteLine("Info: Create {0}", baseName);
            try
            {
                FileStream baseStream = File.Create(baseADT);
                reader = new BinaryReader(baseStream);
                writer = new BinaryWriter(baseStream);
            }
            catch (Exception e)
            {
                Program.ConsoleErrorEnd(e.Message);
                return false;
            }
            #endregion

            foreach (DataChunk dc in chunks)
            {
                if (!allowdChunks.Contains(dc.Signature))
                    continue;

                writer.Write(dc.Data);
            }

            return true;
        }

        private void writeChunk(BinaryWriter writer, string magic, int size, byte[] data = null)
        {
            writer.Write(Helper.MagicToSignature(magic));    // Magic
            writer.Write(size);                              // Size

            if(data != null)
            {
                writer.Write(data);
            }
        }
    }
}
#region aaa
#endregion