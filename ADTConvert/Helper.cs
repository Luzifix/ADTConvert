using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ADTConvert
{
    class Helper
    {
        public static string Reverse(string text)
        {
            if (text == null)
                return null;

            char[] array = text.ToCharArray();
            Array.Reverse(array);
            return new String(array);
        }

        public static UInt32 MagicToSignature(string magic)
        {
            magic = Reverse(magic);
            return BitConverter.ToUInt32(Encoding.ASCII.GetBytes(magic), 0);
        }

        public static bool SeekChunk(BinaryReader reader, string magic, bool begin = true)
        {
            if (begin)
                reader.BaseStream.Seek(0, SeekOrigin.Begin);

            uint signatureInt = MagicToSignature(magic);

            try
            {
                var sig = reader.ReadUInt32();
                while (sig != signatureInt)
                {
                    var size = reader.ReadInt32();
                    reader.BaseStream.Position += size;
                    sig = reader.ReadUInt32();
                }

                return sig == signatureInt;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        public static bool SeekSubChunk(BinaryReader reader, string magic, bool begin = true, long end = 0)
        {
            if (begin)
                reader.BaseStream.Seek(0, SeekOrigin.Begin);

            uint signatureInt = MagicToSignature(magic);

            try
            {
                UInt32 sig = reader.ReadUInt32();
                while (sig != signatureInt)
                {
                    if (end > 0 && reader.BaseStream.Position >= end)
                        break;

                    sig = reader.ReadUInt32();
                }

                return sig == signatureInt;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        public static bool SeekChunkFormList(BinaryReader reader, List<string> magicList, bool begin = true, long end = 0)
        {
            if (begin)
                reader.BaseStream.Seek(0, SeekOrigin.Begin);

            List<UInt32> signatureList = new List<UInt32>();
            foreach (string magic in magicList)
            {
                signatureList.Add(MagicToSignature(magic));
            }

            try
            {
                UInt32 sig = reader.ReadUInt32();
                while (!signatureList.Contains(sig))
                {
                    if (end > 0 && reader.BaseStream.Position >= end)
                        break;

                    sig = reader.ReadUInt32();
                }

                return signatureList.Contains(sig);
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }
}
