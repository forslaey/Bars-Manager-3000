using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace ServiceTool
{
    internal static class ZipIntegrity
    {
        private static readonly uint[] CrcTable = CreateCrcTable();

        // .NET Framework 4.8 ZipArchiveEntry does not expose the stored CRC.
        // The central directory stores it even when the local header uses a data descriptor.
        internal static uint[] ReadChecksums(Stream stream, int entryCount, CancellationToken token)
        {
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                long end = FindDirectoryEnd(reader);
                stream.Position = end + 16;
                long directory = reader.ReadUInt32();
                if (end >= 20)
                {
                    stream.Position = end - 20;
                    if (reader.ReadUInt32() == 0x07064b50)
                    {
                        reader.ReadUInt32();
                        long zip64End = reader.ReadInt64();
                        if (zip64End < 0 || zip64End > end - 20 - 56)
                            throw InvalidDirectory();
                        stream.Position = zip64End;
                        if (reader.ReadUInt32() != 0x06064b50) throw InvalidDirectory();
                        stream.Position = zip64End + 48;
                        directory = reader.ReadInt64();
                        end = zip64End;
                    }
                }
                if (directory < 0 || directory > end) throw InvalidDirectory();
                stream.Position = directory;
                var checksums = new uint[entryCount];
                for (int i = 0; i < checksums.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    long header = stream.Position;
                    if (header > end - 46 || reader.ReadUInt32() != 0x02014b50)
                        throw InvalidDirectory();
                    stream.Position = header + 16;
                    checksums[i] = reader.ReadUInt32();
                    stream.Position = header + 28;
                    int metadataLength = reader.ReadUInt16() + reader.ReadUInt16() + reader.ReadUInt16();
                    if (metadataLength > end - header - 46) throw InvalidDirectory();
                    stream.Position = header + 46 + metadataLength;
                }
                return checksums;
            }
        }

        private static long FindDirectoryEnd(BinaryReader reader)
        {
            const int recordLength = 22;
            var stream = reader.BaseStream;
            int length = (int)Math.Min(stream.Length, UInt16.MaxValue + recordLength);
            stream.Position = stream.Length - length;
            long start = stream.Position;
            byte[] tail = reader.ReadBytes(length);
            long candidate = -1;
            for (int i = tail.Length - recordLength; i >= 0; i--)
            {
                if (tail[i] != 0x50 || tail[i + 1] != 0x4b || tail[i + 2] != 5 || tail[i + 3] != 6)
                    continue;
                int recordEnd = i + recordLength + tail[i + 20] + (tail[i + 21] << 8);
                if (recordEnd == tail.Length) return start + i;
                if (recordEnd < tail.Length && candidate < 0) candidate = start + i;
            }
            if (candidate >= 0) return candidate;
            throw InvalidDirectory();
        }

        private static InvalidDataException InvalidDirectory()
        {
            return new InvalidDataException("Повреждено оглавление ZIP.");
        }

        internal static void CopyEntry(ZipArchiveEntry entry, uint expected, Stream output,
            byte[] buffer, CancellationToken token, Action<int> report = null)
        {
            uint crc = UInt32.MaxValue;
            long length = 0;
            using (var input = entry.Open())
            {
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    length += count;
                    if (length > entry.Length) throw InvalidEntry(entry);
                    for (int i = 0; i < count; i++)
                        crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                    if (output != null) output.Write(buffer, 0, count);
                    if (report != null) report(count);
                }
            }
            token.ThrowIfCancellationRequested();
            if (length != entry.Length || (crc ^ UInt32.MaxValue) != expected) throw InvalidEntry(entry);
        }

        private static InvalidDataException InvalidEntry(ZipArchiveEntry entry)
        {
            return new InvalidDataException("Повреждены данные ZIP: " + entry.FullName);
        }

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320u;
                table[i] = value;
            }
            return table;
        }
    }
}
