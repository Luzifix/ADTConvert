using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        private string[] adtList;
        private string worldName = "";
        private bool worldMCCV = false;
        private bool worldMCLV = false;
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
                watcher.Created += new FileSystemEventHandler(RebuildTables);
                watcher.Renamed += new RenamedEventHandler(RebuildTables);
                watcher.Deleted += new FileSystemEventHandler(RebuildTables);
                watcher.EnableRaisingEvents = true;
                watcher.IncludeSubdirectories = true;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Watcher active");
                Console.ResetColor();

                Console.WriteLine("\nPress ESC to stop the watcher");
                while (Console.ReadKey().Key != ConsoleKey.Escape) { }

                watcher.Dispose();
            }
            else if (inputIsDir && !config.Watch)
            {
                adtList = Directory.GetFiles(config.Input, "*.adt", SearchOption.AllDirectories);

                foreach (string file in adtList)
                {
                    convertADT(file);
                }
            }
            else
            {
                convertADT(config.Input);
            }
        }

        #region ADT
        private void OnADTChanged(object sender, FileSystemEventArgs e)
        {
            if (filesToProcess.Contains(e.FullPath))
            {
                filesToProcess.Remove(e.FullPath);
                return;
            }

            Thread.Sleep(250);
            convertADT(e.FullPath);
            clearADT();

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

            if (inputIsDir)
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

                if (createADTRoot())
                    if (createADTTex())
                    {
                        if (createADTObj())
                        {
                            if (!config.Legion || createADTObjLegion())
                            {
                                #region Create ADT tables
                                if (inputIsDir)
                                {
                                    string newWorldName = Regex.Replace(adtName, @"(_\d{1,}_\d{1,}\.adt)", String.Empty);
                                    if (worldName != newWorldName)
                                    {
                                        worldName = newWorldName;
                                        createTables();
                                    }
                                }
                                #endregion

                                inputReader.Close();
                                return true;
                            }
                        }
                    }

                inputReader.Close();
            }

            return false;
        }

        private bool createADTRoot()
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
                    long mh2oEnd = inputReader.BaseStream.Position- sizeof(uint);
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
                    uint mccvId = inputReader.ReadUInt32();
                    uint mclvId = inputReader.ReadUInt32();
                    rootWriter.Write(mccvId);                                   // ofsMCCV
                    rootWriter.Write(mclvId);                                   // ofsMCLV

                    worldMCCV = (mccvId > 0);
                    worldMCLV = (mclvId > 0);

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
                        if (subChunkName == "MCNR")
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

        private bool createADTTex()
        {
            Console.WriteLine("\n--- Tex ADT Convert ---");
            string ext0 = "_tex0.adt";
            string ext1 = "_tex1.adt";
            BinaryReader texReader = null;
            BinaryWriter texWriter = null;
            List<string> texChunks = new List<string> { "MVER", "MAMP", "MTEX", /*"MCNK"*/ };
            List<string> mcnkSubChunks = new List<string> { "MCLY", "MCSH", "MCAL", "MCMT" };


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
            if (!config.Legion)
            {
                Console.WriteLine("Info: Copy tex0 to tex1");
                string basePath = exportPath + Path.GetFileNameWithoutExtension(adtName);

                File.Delete(basePath + ext1);
                File.Copy(basePath + ext0, basePath + ext1);
            }
            #endregion

            return true;
        }

        private bool createADTObj()
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
            if (!config.Legion)
            {
                Console.WriteLine("Info: Copy obj0 to obj1");
                string basePath = exportPath + Path.GetFileNameWithoutExtension(adtName);

                File.Delete(basePath + ext1);
                File.Copy(basePath + ext0, basePath + ext1);
            }
            #endregion

            return true;
        }

        private bool createADTObjLegion()
        {
            Console.WriteLine("\n--- Legion Obj1 ADT Convert ---");
            string ext1 = "_obj1.adt";
            BinaryReader objReader = null;
            BinaryWriter objWriter = null;
            List<string> objChunks = new List<string> { "MVER", "MMDX", "MMID", "MWMO", "MWID", /*"MDDF", "MODF"*/ };

            if (!createBase(ref objReader, ref objWriter, objChunks, ext1))
                return false;

            #region MDDF
            {

                long mlddStart = 0;
                uint mlddSize = 0;

                #region Copy MDDF to MLDD
                Console.WriteLine("Info: Copy MDDF to MLDD");

                if (Helper.SeekChunk(inputReader, "MDDF"))
                {
                    objWriter.Seek(0, SeekOrigin.End);

                    mlddStart = objWriter.BaseStream.Position;
                    mlddSize = inputReader.ReadUInt32();

                    if (mlddSize > 0)
                    {
                        objWriter.Write(Helper.MagicToSignature("MLDD"));
                        objWriter.Write(mlddSize);
                        objWriter.Write(inputReader.ReadBytes((int)mlddSize));
                    }
                }
                #endregion

                #region Creating MLDX 
                Console.WriteLine("Info: Creating MLDX");
                objWriter.Seek(0, SeekOrigin.End);

                long mldxStart = objWriter.BaseStream.Position;
                uint mldxSize = 28 * (mlddSize / 36);
                objWriter.Write(Helper.MagicToSignature("MLDX"));
                objWriter.Write(mldxSize);

                for (int i = 0; i < mlddSize / 36; i++)
                {
                    // read from MLDD chunk
                    objReader.BaseStream.Position = mlddStart + 16 + (i * 36);

                    float y = Helper.ConvertClientToServer(objReader.ReadSingle());
                    float z = objReader.ReadSingle();
                    float x = Helper.ConvertClientToServer(objReader.ReadSingle());

                    // Go to file end
                    objWriter.Seek(0, SeekOrigin.End);

                    // Lower C3Vector for the CAaBox
                    objWriter.Write(x - (config.LegionBoundingBox / 2));
                    objWriter.Write(y - (config.LegionBoundingBox / 2));
                    objWriter.Write(z - (config.LegionBoundingBox / 2));

                    // Upper C3Vector for the CAaBox
                    objWriter.Write(x + (config.LegionBoundingBox / 2));
                    objWriter.Write(y + (config.LegionBoundingBox / 2));
                    objWriter.Write(z + (config.LegionBoundingBox / 2));

                    // Radius
                    objWriter.Write(config.LegionBoundingBox);
                }
                #endregion

                #region Creating MLDL 
                Console.WriteLine("Info: Creating empty MLDL");
                objWriter.Seek(0, SeekOrigin.End);

                long mldlStart = objWriter.BaseStream.Position;
                uint mldlSize = 0;
                objWriter.Write(Helper.MagicToSignature("MLDL"));
                objWriter.Write(mldlSize);
                #endregion
            }
            #endregion

            #region MODF
            if (true)
            {
                long mldlStart = 0;
                uint mldlSize = 0;
                #region Copy MODF to MLMD & clean bounding box
                Console.WriteLine("Info: Copy MODF to MLMD");

                if (Helper.SeekChunk(inputReader, "MODF"))
                {
                    objWriter.Seek(0, SeekOrigin.End);
                    uint modfSize = inputReader.ReadUInt32();

                    mldlStart = objWriter.BaseStream.Position;
                    mldlSize = modfSize - ((modfSize / 64) * 24);

                    if (modfSize > 0)
                    {
                        objWriter.Write(Helper.MagicToSignature("MLMD"));
                        objWriter.Write(mldlSize);

                        for (int i = 0; i < modfSize / 64; i++)
                        {
                            objWriter.Write(inputReader.ReadUInt32()); // nameId
                            objWriter.Write(inputReader.ReadUInt32()); // uniqueId
                            objWriter.Write(inputReader.ReadSingle()); // position_x
                            objWriter.Write(inputReader.ReadSingle()); // position_y
                            objWriter.Write(inputReader.ReadSingle()); // position_z
                            objWriter.Write(inputReader.ReadSingle()); // rotation_x
                            objWriter.Write(inputReader.ReadSingle()); // rotation_y
                            objWriter.Write(inputReader.ReadSingle()); // rotation_z
                            inputReader.BaseStream.Seek(sizeof(float) * 6, SeekOrigin.Current); // extents
                            objWriter.Write(inputReader.ReadUInt16()); // flags
                            objWriter.Write(inputReader.ReadUInt16()); // doodadSet
                            objWriter.Write(inputReader.ReadUInt16()); // nameSet
                            objWriter.Write(inputReader.ReadUInt16()); // unk
                        }
                    }
                }
                #endregion

                #region Creating MLMX 
                Console.WriteLine("Info: Creating MLMX");
                objWriter.Seek(0, SeekOrigin.End);

                long mlmxStart = objWriter.BaseStream.Position;
                uint mlmxSize = 28 * (mldlSize / 40);
                objWriter.Write(Helper.MagicToSignature("MLMX"));
                objWriter.Write(mlmxSize);

                for (int i = 0; i < mldlSize / 40; i++)
                {
                    // read from MLMD chunk
                    objReader.BaseStream.Position = mldlStart + 16 + (i * 40);

                    float y = Helper.ConvertClientToServer(objReader.ReadSingle());
                    float z = objReader.ReadSingle();
                    float x = Helper.ConvertClientToServer(objReader.ReadSingle());

                    // Go to file end
                    objWriter.Seek(0, SeekOrigin.End);

                    // Lower C3Vector for the CAaBox
                    objWriter.Write(x - (config.LegionBoundingBox / 2));
                    objWriter.Write(y - (config.LegionBoundingBox / 2));
                    objWriter.Write(z - (config.LegionBoundingBox / 2));

                    // Upper C3Vector for the CAaBox
                    objWriter.Write(x + (config.LegionBoundingBox / 2));
                    objWriter.Write(y + (config.LegionBoundingBox / 2));
                    objWriter.Write(z + (config.LegionBoundingBox / 2));

                    // Radius
                    objWriter.Write(config.LegionBoundingBox);
                }
                #endregion
            }
            #endregion

            objWriter.Close();
            objReader.Close();

            return true;
        }

        #endregion

        #region WDT, WDL & TEX
        private void RebuildTables(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(250);
            createTables();
        }

        private void createTables()
        {
            if (config.NoTables)
            {
                return;
            }

            createTex();
            createWDT();
            createWDTlgt();
            createWDTocc();
            createWDL();
        }

        private void createTex()
        {
            Console.WriteLine("\n--- Create Tex ---");
            BinaryReader texReader = null;
            BinaryWriter texWriter = null;

            createFile(ref texReader, ref texWriter, worldName + ".tex");

            #region TXVR
            writeChunk(texWriter, "TXVR", 4);
            texWriter.Write(new byte[4]);
            #endregion

            texReader.Close();
            texWriter.Close();
        }

        [FlagsAttribute]
        enum MPHDFlags : uint
        {
            wdt_uses_global_map_obj = 0x0001,               // Use global map object definition.
            adt_has_mccv = 0x0002,                          // ≥ Wrath adds color: ADT.MCNK.MCCV. with this flag every ADT in the map _must_ have MCCV chunk at least with default values, else only base texture layer is rendered on such ADTs.
            adt_has_big_alpha = 0x0004,                     // shader = 2. Decides whether to use _env terrain shaders or not: funky and if MCAL has 4096 instead of 2048(?)
            adt_has_doodadrefs_sorted_by_size_cat = 0x0008, // if enabled, the ADT's MCRF(m2 only)/MCRD chunks need to be sorted by size category
            adt_has_mclv = 0x0010,                          // ≥ Cata adds second color: ADT.MCNK.MCLV
            adt_has_upside_down_ground = 0x0020,            // ≥ Cata Flips the ground display upside down to create a ceiling
            unk_0x0040 = 0x0040,                            // ≥ Mists ??? -- Only found on Firelands2.wdt (but only since MoP) before Legion
            adt_has_height_texturing = 0x0080,              // ≥ Mists shader = 6. Decides whether to influence alpha maps by _h+MTXP: (without with) also changes MCAL size to 4096 for uncompressed entries
            unk_0x0100 = 0x0100,                            // ≥ Legion implicitly sets 0x8000
            unk_0x0200 = 0x0200,
            unk_0x0400 = 0x0400,
            unk_0x0800 = 0x0800,
            unk_0x1000 = 0x1000,
            unk_0x2000 = 0x2000,
            unk_0x4000 = 0x4000,
            unk_0x8000 = 0x8000,                            // ≥ Legion implicitly set for map ids 0, 1, 571, 870, 1116 (continents). Affects the rendering of _lod.adt
        };

        [FlagsAttribute]
        enum MAINFlags : uint
        {
            has_adt = 0x0001,
            all_water = 0x0002,
            loaded = 0x0004
        };

        private void createWDT()
        {
            Console.WriteLine("\n--- Create WDT ---");
            BinaryReader wdtReader = null;
            BinaryWriter wdtWriter = null;

            createFile(ref wdtReader, ref wdtWriter, worldName + ".wdt");

            #region MVER
            Console.WriteLine("Info: Create MVER");
            writeChunk(wdtWriter, "MVER", 4);
            wdtWriter.Write((uint)18);
            #endregion

            #region MPHD
            Console.WriteLine("Info: Create MPHD");
            MPHDFlags mphdFlags = MPHDFlags.adt_has_doodadrefs_sorted_by_size_cat | MPHDFlags.adt_has_mclv | MPHDFlags.unk_0x0040;

            if (worldMCCV)
            {
                mphdFlags |= MPHDFlags.adt_has_mccv;
            }

            if (config.Legion)
            {
                mphdFlags |= MPHDFlags.unk_0x0100;
            }

            writeChunk(wdtWriter, "MPHD", 32);
            wdtWriter.Write((uint)mphdFlags);
            wdtWriter.Write((uint)0); // unk_1
            wdtWriter.Write((uint)0); // unk_2
            wdtWriter.Write((uint)0); // unk_3                 
            wdtWriter.Write((uint)0); // unk_4                    
            wdtWriter.Write((uint)0); // unk_5                      
            wdtWriter.Write((uint)0); // unk_6                      
            wdtWriter.Write((uint)0); // unk_7                      
            #endregion

            #region MAIN
            Console.WriteLine("Info: Create MAIN");
            MAINFlags mainFlags;

            writeChunk(wdtWriter, "MAIN", 8 * (64 * 64));
            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    string fileName = worldName + "_" + y.ToString().PadLeft(2, '0') + "_" + x.ToString().PadLeft(2, '0') + ".adt";

                    if (adtList.Any(item => item.EndsWith(fileName)))
                    {
                        mainFlags = MAINFlags.has_adt;
                    }
                    else
                    {
                        mainFlags = MAINFlags.all_water;
                    }

                    wdtWriter.Write((uint)mainFlags); // flags
                    wdtWriter.Write((uint)0); // asyncId
                }
            }
            #endregion

            wdtReader.Close();
            wdtWriter.Close();
        }

        private void createWDTlgt()
        {
            Console.WriteLine("\n--- Create Lgt ---");
            BinaryReader lgtReader = null;
            BinaryWriter lgtWriter = null;

            createFile(ref lgtReader, ref lgtWriter, worldName + "_lgt.wdt");

            #region MVER
            writeChunk(lgtWriter, "MVER", 4);
            lgtWriter.Write((uint)18);
            #endregion

            lgtReader.Close();
            lgtWriter.Close();
        }

        private void createWDTocc()
        {
            Console.WriteLine("\n--- Create occ ---");
            BinaryReader occReader = null;
            BinaryWriter occWriter = null;

            createFile(ref occReader, ref occWriter, worldName + "_occ.wdt");

            #region MVER
            writeChunk(occWriter, "MVER", 4);
            occWriter.Write((uint)18);
            #endregion

            #region MAOI
            Console.WriteLine("Info: Create MAOI");
            writeChunk(occWriter, "MAOI", 12 * adtList.Length);

            uint i = 0;
            foreach (var adt in adtList)
            {
                string name = Path.GetFileNameWithoutExtension(adt);
                var match = Regex.Match(name, @"_(\d{1,})_(\d{1,})$");
                UInt16 y = UInt16.Parse(match.Groups[1].ToString());
                UInt16 x = UInt16.Parse(match.Groups[2].ToString());

                occWriter.Write(x); // tile_x
                occWriter.Write(y); // tile_y
                occWriter.Write(1090 * i++); // offset
                occWriter.Write((uint)1090); // size
            }
            #endregion

            #region MAOH
            Console.WriteLine("Info: Create MAOH");
            int moahSize = 1090 * adtList.Length;
            writeChunk(occWriter, "MAOH", moahSize, new byte[moahSize]);
            #endregion

            occReader.Close();
            occWriter.Close();
        }

        private void createWDL()
        {
            Console.WriteLine("\n--- Create WDL ---");
            BinaryReader wdtReader = null;
            BinaryWriter wdtWriter = null;

            createFile(ref wdtReader, ref wdtWriter, worldName + ".wdl");

            #region MVER
            Console.WriteLine("Info: Create MVER");
            writeChunk(wdtWriter, "MVER", 4);
            wdtWriter.Write((uint)18);
            #endregion

            #region MWMO
            Console.WriteLine("Info: Create MWMO");
            writeChunk(wdtWriter, "MWMO", 0);
            #endregion

            #region MWID
            Console.WriteLine("Info: Create MWID");
            writeChunk(wdtWriter, "MWID", 0);
            #endregion

            #region MODF
            Console.WriteLine("Info: Create MODF");
            writeChunk(wdtWriter, "MODF", 0);
            #endregion

            #region Get Data for MAOF & MARE
            wdtWriter.Seek(0, SeekOrigin.End);
            Console.WriteLine("Info: Get Data for MAOF & MARE");
            int[] maofValues = new int[4096];
            List<short[]> heightValues = new List<short[]>();

            // current postion + moaf size + moaf header
            int startOffset = (int)wdtWriter.BaseStream.Position + 4096 * 4 + 8;
            int nextMARE = 1138;
            for (var i = 0; i < 64 * 64; ++i)
            {
                short[] heights = GetMareEntry(worldName, i % 64, i / 64);
                if (heights == null)
                {
                    maofValues[i] = 0;
                }
                else
                {
                    maofValues[i] = startOffset;
                    startOffset += nextMARE;
                    heightValues.Add(heights);
                }
            }
            #endregion

            #region MAOF, MARE, MAOE & MAHO
            Console.WriteLine("Info: Create MAOF, MARE & MAHO");
            writeChunk(wdtWriter, "MAOF", maofValues.Length * 4);
            wdtWriter.WriteArray(maofValues);

            foreach (var heights in heightValues)
            {
                writeChunk(wdtWriter, "MARE", heights.Length * 2);
                wdtWriter.WriteArray(heights);

                int mahoSize = 32;
                writeChunk(wdtWriter, "MAHO", mahoSize, new byte[mahoSize]);
            }
            #endregion

            wdtReader.Close();
            wdtWriter.Close();
        }

        #region Get Mare entry from Neos
        private short[] GetMareEntry(string continent, int ax, int ay)
        {
            var retValue = new short[17 * 17 + 16 * 16];
            var filePath = string.Format(@"{0}\{1}_{2}_{3}.adt", config.Input, continent, ax.ToString().PadLeft(2, '0'), ay.ToString().PadLeft(2, '0'));

            if (!File.Exists(filePath))
            {
                return null;
            }

            using (var strm = File.OpenRead(filePath))
            {
                var reader = new BinaryReader(strm);
                var heights = new float[256][];

                for (var i = 0; i < 16; ++i)
                {
                    for (var j = 0; j < 16; ++j)
                    {
                        float baseHeight;
                        int chunkSize;
                        // if there ain't no MCNK entry for a chunk in an ADT somethings
                        // gone really wrong. Not having an MCVT in an MCNK is different,
                        // but when there isn't any MCNK skip the ADT.
                        if (!SeekNextMcnk(reader, out baseHeight, out chunkSize))
                            return null;

                        var curPos = reader.BaseStream.Position;

                        if (!MoveToMcvt(reader))
                            continue;

                        heights[i * 16 + j] = reader.ReadArray<float>(145);
                        for (var k = 0; k < heights[i * 16 + j].Length; ++k)
                            heights[i * 16 + j][k] += baseHeight;

                        reader.BaseStream.Position = curPos + chunkSize;
                    }
                }

                const float stepSize = Metrics.TileSize / 16.0f;
                for (var i = 0; i < 17; ++i)
                {
                    for (var j = 0; j < 17; ++j)
                    {
                        var posx = j * stepSize;
                        var posy = i * stepSize;

                        retValue[i * 17 + j] = (short)
                            Math.Min(
                                Math.Max(
                                    Math.Round(GetLandHeight(heights, posx, posy)),
                                    short.MinValue),
                                short.MaxValue);
                    }
                }

                for (var i = 0; i < 16; ++i)
                {
                    for (var j = 0; j < 16; ++j)
                    {
                        var posx = j * stepSize;
                        var posy = i * stepSize;
                        posx += stepSize / 2.0f;
                        posy += stepSize / 2.0f;

                        retValue[17 * 17 + i * 16 + j] = (short)
                            Math.Min(
                                Math.Max(
                                    Math.Round(GetLandHeight(heights, posx, posy)),
                                    short.MinValue),
                                short.MaxValue);
                    }
                }
            }

            return retValue;
        }

        private float GetLandHeight(float[][] heights, float x, float y)
        {
            var cx = (int)Math.Floor(x / Metrics.ChunkSize);
            var cy = (int)Math.Floor(y / Metrics.ChunkSize);
            cx = Math.Min(Math.Max(cx, 0), 15);
            cy = Math.Min(Math.Max(cy, 0), 15);

            if (heights[cy * 16 + cx] == null)
                return 0;

            x -= cx * Metrics.ChunkSize;
            y -= cy * Metrics.ChunkSize;

            var row = (int)(y / (Metrics.UnitSize * 0.5f) + 0.5f);
            var col = (int)((x - Metrics.UnitSize * 0.5f * (row % 2)) / Metrics.UnitSize + 0.5f);

            if (row < 0 || col < 0 || row > 16 || col > (((row % 2) != 0) ? 8 : 9))
                return 0;

            return heights[cy * 16 + cx][17 * (row / 2) + (((row % 2) != 0) ? 9 : 0) + col];
        }

        private static bool SeekNextMcnk(BinaryReader reader, out float baseHeight, out int chunkSize)
        {
            chunkSize = 0;
            baseHeight = 0;
            try
            {
                var signature = reader.ReadUInt32();
                var size = reader.ReadInt32();

                while (signature != 0x4D434E4B)
                {
                    reader.ReadBytes(size);
                    signature = reader.ReadUInt32();
                    size = reader.ReadInt32();
                }

                var mcnk = reader.Read<IO.Files.Terrain.Wotlk.Mcnk>();
                baseHeight = mcnk.Position.z;
                reader.BaseStream.Position -= SizeCache<IO.Files.Terrain.Wotlk.Mcnk>.Size;

                chunkSize = size;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool MoveToMcvt(BinaryReader reader)
        {
            reader.BaseStream.Position -= 4;
            var size = reader.ReadInt32();

            var mcnk = reader.Read<IO.Files.Terrain.Wotlk.Mcnk>();
            if (mcnk.Mcvt < 8 + SizeCache<IO.Files.Terrain.Wotlk.Mcnk>.Size)
                return false;

            var toRead = mcnk.Mcvt - SizeCache<IO.Files.Terrain.Wotlk.Mcnk>.Size;
            reader.ReadBytes(toRead);
            return true;
        }
        #endregion
        #endregion

        private bool createFile(ref BinaryReader reader, ref BinaryWriter writer, string baseName)
        {
            Console.WriteLine("Info: Create {0}", baseName);

            string filePath = exportPath + baseName;

            #region Remove adt
            if (File.Exists(filePath))
            {
                if (config.Verbose)
                    Console.WriteLine("Debug: Remove {0}", baseName);

                File.Delete(filePath);
            }
            #endregion

            try
            {
                FileStream baseStream = File.Create(filePath);
                reader = new BinaryReader(baseStream);
                writer = new BinaryWriter(baseStream);
            }
            catch (Exception e)
            {
                Program.ConsoleErrorEnd(e.Message);
                return false;
            }

            return true;
        }

        private bool createBase(ref BinaryReader reader, ref BinaryWriter writer, List<string> allowdChunks, string ext)
        {
            string baseName = Path.GetFileNameWithoutExtension(adtName) + ext;

            // Create file
            createFile(ref reader, ref writer, baseName);

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

            if (data != null)
            {
                writer.Write(data);
            }
        }

        private void clearADT()
        {
            adtName = "";
            exportPath = "";
            chunks.Clear();

            if (inputReader != null)
            {
                inputReader.Close();
                inputReader = null;
            }
        }
    }
}
