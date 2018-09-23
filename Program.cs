using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BNTC
{
    internal class Program
    {
        public const int Magic = 0x43544e42;

        public static byte[] Key = 
        {
            //REDACTED
        };

        public static byte[] IV =
        {
            //REDACTED
        };

        public static byte[] Crypt(byte[] Key, byte[] IV, byte[] Data, bool ForEncryption)
        {
            var Aes = new AesCryptoServiceProvider()
            {
                Key = Key,
                IV = IV,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7
            };
            if (ForEncryption)
            {
                return Aes.CreateEncryptor().TransformFinalBlock(Data, 0, Data.Length);
            }
            else
            {
                return Aes.CreateDecryptor().TransformFinalBlock(Data, 0, Data.Length);
            }
        }

        public static byte[] Compress(byte[] Input)
        {
            using (MemoryStream Strm = new MemoryStream())
            {
                using (GZipStream CBytes = new GZipStream(Strm, CompressionMode.Compress, true))
                {
                    CBytes.Write(Input, 0, Input.Length);
                }
                return Strm.ToArray();
            }
        }

        private static byte[] Decompress(byte[] Input)
        {
            using (GZipStream CBytes = new GZipStream(new MemoryStream(Input), CompressionMode.Decompress))
            {
                const int Size = 262144;
                byte[] Buf = new byte[Size];
                using (MemoryStream Strm = new MemoryStream())
                {
                    int Counter = 0;
                    do
                    {
                        Counter = CBytes.Read(Buf, 0, Size);
                        if (Counter > 0)
                        {
                            Strm.Write(Buf, 0, Counter);
                        }
                    }
                    while (Counter > 0);
                    return Strm.ToArray();
                }
            }
        }

        public static byte[] BufferedRead(Stream Input)
        {
            byte[] Buf = new byte[262144];
            using (MemoryStream Strm = new MemoryStream())
            {
                int Rd;
                while ((Rd = Input.Read(Buf, 0, Buf.Length)) > 0)
                {
                    Strm.Write(Buf, 0, Rd);
                }
                return Strm.ToArray();
            }
        }

        private static void Main(string[] args)
        {
            void Pack()
            {
                var Strm = new MemoryStream();
                var Writer = new BinaryWriter(Strm);

                Writer.Write(Magic);

                var FileNameList = new List<string>();
                int FileNameOffsets = 0;

                foreach (var file in Directory.GetFiles(args[1], "*", SearchOption.AllDirectories))
                {
                    FileNameList.Add($"{Path.GetDirectoryName(file)}\\{Path.GetFileName(file)}");
                }

                Writer.Write((long)0);
                Writer.Write(FileNameList.ToArray().Length);

                Console.WriteLine();

                foreach (var file in FileNameList)
                {
                    var InFile = Compress(BufferedRead(File.OpenRead(file)));
                    var FullName = $"{Path.GetDirectoryName(file)}\\{Path.GetFileName(file)}";
                    Console.WriteLine($"Compressing and adding {FullName} to BNTC...");
                    Writer.Write(InFile.Length);
                    Writer.Write(FileNameOffsets);
                    FileNameOffsets += FullName.Length + 1;
                    Writer.Write(InFile);
                }

                Console.WriteLine("\nWriting string table...");

                var CurPos = Writer.BaseStream.Position;
                Writer.BaseStream.Position = 4;
                Writer.Write(Writer.BaseStream.Length);
                Writer.BaseStream.Position = CurPos;

                foreach (var filename in FileNameList)
                {
                    Writer.Write(Encoding.ASCII.GetBytes(filename));
                    Writer.Write((byte)0); // Null-terminate
                }
                Console.WriteLine("\nEncrypting...");
                File.WriteAllBytes(args[2]+"_encrypted.bntc", Crypt(Key, IV, Strm.ToArray(), true));
                Writer.Close();
                Strm.Close();
                Console.WriteLine("\nDone!");
            }

            void Unpack()
            {
                var Strm = File.ReadAllBytes(args[1]);
                Console.WriteLine("\nDecrypting...");
                var RdStrm = new MemoryStream(Crypt(Key, IV, Strm, false));
                var Reader = new BinaryReader(RdStrm);

                if (Reader.ReadInt32() == Magic)
                {
                    var OffsetToStringTable = Reader.ReadInt64();
                    var NumOfFiles = Reader.ReadInt32();
                    Console.WriteLine();
                    for (int i = 0; i < NumOfFiles; i++)
                    {
                        var FileLength = Reader.ReadInt32();
                        var StringOfs = Reader.ReadInt32();
                        var FileStartPosition = Reader.BaseStream.Position;
                        RdStrm.Position = OffsetToStringTable + StringOfs;
                        var ReadAllStrings = new StreamReader(RdStrm).ReadToEnd();
                        var Filename = ReadAllStrings.Substring(0, Math.Max(0, ReadAllStrings.IndexOf('\0')));
                        Console.WriteLine($"Extracting {Filename}...");
                        RdStrm.Position = FileStartPosition;
                        Directory.CreateDirectory(Filename.Substring(0, Math.Max(0, Filename.LastIndexOf('\\'))));
                        var WriteOut = File.OpenWrite($@"{Filename}");
                        var Outfile = Decompress(Reader.ReadBytes(FileLength));
                        WriteOut.Write(Outfile, 0, Outfile.Length);
                    }
                    Console.WriteLine("\nDone!");
                }
                else
                {
                    Console.WriteLine("Error: this is not a valid BNTC archive!");
                }
            }

            var Usage = "Usage: BNTC <-pack (-pk, -a) / -unpack (-upk, -x)> <Input file / directory> [Output file (if packing)]";

            if (args.Length >= 2)
            {
                if (args[0] == "-pack" || args[0] == "-pk" || args[0] == "-a" && args.Length == 3)
                {
                    Pack();
                }
                else if (args[0] == "-unpack" || args[0] == "-upk" || args[0] == "-x" && args.Length == 2)
                {
                    Unpack();
                }
                else
                {
                    Console.WriteLine(Usage);
                }
            }
            else
            {
                Console.WriteLine(Usage);
            }
        }
    }
}