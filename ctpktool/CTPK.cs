﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace ctpktool
{
    class Ctpk
    {
        private const int Magic = 0x4B505443; // 'CTPK'

        public ushort Version; // ?
        public ushort NumberOfTextures;
        public uint TextureSectionOffset;
        public uint TextureSectionSize;
        public uint HashSectionOffset;
        public uint TextureInfoSection; // ??


        public long Size; //not part of format

        private readonly List<CTPKEntry> _entries;

        public Ctpk()
        {
            _entries = new List<CTPKEntry>();
            Version = 1;
        }

        private int CalculatePadding(int max, int cur)
        {
            int padding = max - cur % max;

            if (padding == max)
                padding = 0;

            return padding;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(new byte[0x20]); // Write 0x20 bytes of blank data so we can come back to it later

            // Make sure all sections have a unique IndexA
            // If not, give them one
            _entries.Sort((x, y) => x.FileIndexA.CompareTo(y.FileIndexA));
            var fileIndexes = _entries.Select(x => x.FileIndexA);
            int nextFileIndex = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].FileIndexA == -1)
                {
                    while (fileIndexes.Contains(nextFileIndex))
                    {
                        nextFileIndex++;
                    }

                    _entries[i].FileIndexA = nextFileIndex;
                }
            }

            // Do the same as above, but for FileIndexB
            _entries.Sort((x, y) => x.FileIndexB.CompareTo(y.FileIndexB));
            fileIndexes = _entries.Select(x => x.FileIndexB);
            nextFileIndex = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].FileIndexB == -1)
                {
                    while (fileIndexes.Contains(nextFileIndex))
                    {
                        nextFileIndex++;
                    }

                    _entries[i].FileIndexB = nextFileIndex;
                }
            }

            // Section 1
            _entries.Sort((x, y) => x.FileIndexA.CompareTo(y.FileIndexA));
            foreach (var entry in _entries)
            {
                entry.Write(writer);
            }

            // Section 2
            foreach (var entry in _entries)
            {
                writer.Write(entry.Info);
            }

            // Section 3
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var curOffset = writer.BaseStream.Position;

                // Fix filename offset in section 1
                writer.BaseStream.Seek(0x20 * (i + 1), SeekOrigin.Begin);
                writer.Write((uint)curOffset);
                writer.BaseStream.Seek(curOffset, SeekOrigin.Begin);

                writer.Write(Encoding.GetEncoding(932).GetBytes(entry.InternalFilePath));
                writer.Write((byte)0); // Null terminated
            }

            writer.Write(new byte[CalculatePadding(4, (int)writer.BaseStream.Length)]); // Pad the filename section to the nearest 4th byte

            // Section 4
            _entries.Sort((x, y) => x.FileIndexB.CompareTo(y.FileIndexB));
            HashSectionOffset = (uint)writer.BaseStream.Length;
            foreach (var entry in _entries)
            {
                writer.Write(entry.FilenameHash);
                writer.Write(entry.FileIndexA);
            }

            // Section 5
            _entries.Sort((x, y) => x.FileIndexA.CompareTo(y.FileIndexA));
            TextureInfoSection = (uint)writer.BaseStream.Length;
            foreach (var entry in _entries)
            {
                writer.Write(entry.Info2);
            }

            writer.Write(new byte[CalculatePadding(0x80, (int)writer.BaseStream.Length)]); // Pad the filename section to the nearest 0x80th byte

            // Section 6
            TextureSectionOffset = (uint)writer.BaseStream.Length;
            TextureSectionSize = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var curOffset = writer.BaseStream.Position;

                // Fix texture data offset in section 1
                writer.BaseStream.Seek(0x20 * (i + 1) + 0x08, SeekOrigin.Begin);
                writer.Write(TextureSectionSize);
                writer.BaseStream.Seek(curOffset, SeekOrigin.Begin);

                writer.Write(entry.TextureRawData);

                TextureSectionSize += (uint)entry.TextureRawData.Length;
            }

            NumberOfTextures = (ushort)_entries.Count;

            writer.BaseStream.Seek(0, SeekOrigin.Begin);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(NumberOfTextures);
            writer.Write(TextureSectionOffset);
            writer.Write(TextureSectionSize);
            writer.Write(HashSectionOffset);
            writer.Write(TextureInfoSection);
        }

        public static Ctpk Create(string inputPath, string outputPath)
        {
            if (!Directory.Exists(inputPath))
            {
                return null;
            }

            Ctpk file = new Ctpk();

            // Look for all xml definition files in the folder
            var files = Directory.GetFiles(inputPath, "*.xml", SearchOption.AllDirectories);
            foreach (var xmlFilename in files)
            {
                CTPKEntry entry = CTPKEntry.FromFile(xmlFilename, inputPath);
                file._entries.Add(entry);
            }

            for (int i = 0; i < file._entries.Count; i++)
            {
                file._entries[i].BitmapSizeOffset = (uint)((file._entries.Count + 1) * 8 + i);
            }

            using (BinaryWriter writer = new BinaryWriter(File.Open(outputPath, FileMode.Create)))
            {
                file.Write(writer);
            }

            Console.WriteLine("Finished! Saved to {0}", outputPath);

            return file;
        }

        public static Ctpk Read(string inputPath, string outputPath, bool isRawExtract = false, bool outputInfo = false)
        {
            if (!File.Exists(inputPath))
            {
                return null;
            }

            using (BinaryReader reader = new BinaryReader(File.Open(inputPath, FileMode.Open)))
            {
                var data = new byte[reader.BaseStream.Length];
                reader.Read(data, 0, data.Length);
                return Read(data, inputPath, outputPath, isRawExtract, outputInfo);
            }
        }

        public static void ReadGOG(string inputPath, string outputPath, bool isRawExtract = false, bool outputInfo = false)
        {
            if (!File.Exists(inputPath))
            {
                return;
            }

            using (BinaryReader reader = new BinaryReader(File.Open(inputPath, FileMode.Open)))
            {
                while (reader.BaseStream.Length - reader.BaseStream.Position >= 4)
                {
                    var hdrStart = reader.ReadUInt32();
                    reader.BaseStream.Seek(-4, SeekOrigin.Current); //peek the current 4 bytes
                    if (hdrStart == Magic)
                    {
                        Console.WriteLine("Found a CTPK in GOG");
                        var data = new byte[reader.BaseStream.Length - reader.BaseStream.Position];
                        long resetTo = reader.BaseStream.Position + 1;
                        reader.Read(data, 0, data.Length);
                        reader.BaseStream.Seek(resetTo, SeekOrigin.Begin);
                        Ctpk it = Read(data, inputPath, outputPath, isRawExtract, outputInfo);
                        Console.WriteLine("Total CTPK bytes: {0}", it.Size);
                    }
                    else
                    {
                        reader.ReadByte(); //yup
                    }
                }
            }
        }

        public static Ctpk Read(byte[] data, string inputPath, string outputPath, bool isRawExtract = false, bool outputInfo = false)
        {
            Ctpk file = new Ctpk();
            long BeginningPos;
            using (MemoryStream dataStream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(dataStream))
            {
                BeginningPos = reader.BaseStream.Position;
                if (reader.ReadUInt32() != Magic)
                {
                    Console.WriteLine("ERROR: Not a valid CTPK file.");
                }

                file.Version = reader.ReadUInt16();
                file.NumberOfTextures = reader.ReadUInt16();
                file.TextureSectionOffset = reader.ReadUInt32();
                file.TextureSectionSize = reader.ReadUInt32();
                file.HashSectionOffset = reader.ReadUInt32();
                file.TextureInfoSection = reader.ReadUInt32();

                // Section 1 + 3
                for (int i = 0; i < file.NumberOfTextures; i++)
                {
                    reader.BaseStream.Seek(0x20 * (i + 1), SeekOrigin.Begin);

                    CTPKEntry entry = CTPKEntry.Read(reader);
                    entry.FileIndexA = file._entries.Count;
                    file._entries.Add(entry);
                }

                // Section 2
                for (int i = 0; i < file.NumberOfTextures; i++)
                {
                    file._entries[i].Info = reader.ReadUInt32();
                }

                // Section 4
                reader.BaseStream.Seek(file.HashSectionOffset, SeekOrigin.Begin);
                for (int i = 0; i < file.NumberOfTextures; i++)
                {
                    file._entries[i].FilenameHash = reader.ReadUInt32();

                    int idx = reader.ReadInt32();
                    if (idx < file._entries.Count)
                    {
                        file._entries[idx].FileIndexB = i;
                    }
                    else
                    {
                        Console.WriteLine("ERROR(?): Found hash entry without a matching file entry");
                    }
                }

                // Section 5
                reader.BaseStream.Seek(file.TextureInfoSection, SeekOrigin.Begin);
                for (int i = 0; i < file.NumberOfTextures; i++)
                {
                    file._entries[i].Info2 = reader.ReadUInt32();
                }

                // Section 6
                for (int i = 0; i < file.NumberOfTextures; i++)
                {
                    reader.BaseStream.Seek(file.TextureSectionOffset + file._entries[i].TextureOffset, SeekOrigin.Begin);
                    file._entries[i].TextureRawData = new byte[file._entries[i].TextureSize];
                    reader.Read(file._entries[i].TextureRawData, 0, (int)file._entries[i].TextureSize);
                }

                for (int i = 0; i < file.NumberOfTextures; i++)
                {
                    Console.WriteLine("Converting {0}...", file._entries[i].InternalFilePath);
                    file._entries[i].ToFile(outputPath, isRawExtract, outputInfo);
                }

                file.Size = reader.BaseStream.Position - BeginningPos;
            }
            return file;
        }
    }
}
