using System;
using System.IO;
using System.Text;
using Forens.Core.Collectors.Prefetch;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class PrefetchParserTests
    {
        [Fact]
        public void Parse_returns_error_for_too_short_buffer()
        {
            var info = PrefetchParser.Parse(new byte[10]);
            Assert.NotNull(info.ParseError);
        }

        [Fact]
        public void Parse_detects_bad_SCCA_signature()
        {
            // 84-byte buffer, version 30 in header, but signature is 0x00000000 not "SCCA"
            byte[] data = new byte[84];
            BitConverter.GetBytes(30u).CopyTo(data, 0);
            // signature stays zero
            var info = PrefetchParser.Parse(data);
            Assert.Contains("SCCA", info.ParseError);
        }

        [Fact]
        public void Parse_extracts_executable_name_and_pathhash_from_synthetic_v30_header()
        {
            byte[] data = BuildMinimalSccaPayload(version: 30, exeName: "TEST.EXE", pathHash: 0xCAFEBABEu);
            var info = PrefetchParser.Parse(data);
            Assert.Null(info.ParseError);
            Assert.Equal(30u, info.FormatVersion);
            Assert.Equal("TEST.EXE", info.ExecutableName);
            Assert.Equal(0xCAFEBABEu, info.PathHash);
        }

        [Fact]
        public void Parse_v30_extracts_run_count_and_last_run_time()
        {
            byte[] data = BuildMinimalSccaPayload(version: 30, exeName: "TEST.EXE", pathHash: 0xDEADBEEFu);

            // 8 last-run FILETIMEs at 0x80
            long ft = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(ft).CopyTo(data, 0x80);
            // Run count at 0xD0
            BitConverter.GetBytes(42u).CopyTo(data, 0xD0);

            var info = PrefetchParser.Parse(data);
            Assert.Null(info.ParseError);
            Assert.Equal((uint?)42, info.RunCount);
            Assert.Single(info.LastRunTimesUtc);
            Assert.Equal(2026, info.LastRunTimesUtc[0].Year);
            Assert.Equal(1, info.LastRunTimesUtc[0].Month);
        }

        [Fact]
        public void Parse_extracts_filename_strings_and_picks_executable_full_path()
        {
            byte[] data = BuildMinimalSccaPayload(version: 30, exeName: "NOTEPAD.EXE", pathHash: 0u);

            // Build filename strings section: 3 UTF-16-LE strings, each null-terminated.
            string[] strings =
            {
                @"\VOLUME{abc}\WINDOWS\SYSTEM32\NTDLL.DLL",
                @"\VOLUME{abc}\WINDOWS\SYSTEM32\NOTEPAD.EXE",
                @"\VOLUME{abc}\WINDOWS\SYSTEM32\KERNEL32.DLL"
            };
            byte[] section = BuildStringsSection(strings);
            int sectionOffset = data.Length;
            Array.Resize(ref data, sectionOffset + section.Length);
            section.CopyTo(data, sectionOffset);

            // filenameStringsOffset / filenameStringsSize at 0x64 / 0x68
            BitConverter.GetBytes((uint)sectionOffset).CopyTo(data, 0x64);
            BitConverter.GetBytes((uint)section.Length).CopyTo(data, 0x68);

            var info = PrefetchParser.Parse(data);
            Assert.Null(info.ParseError);
            Assert.Equal(3, info.ReferencedFileCount);
            Assert.Equal(3, info.ReferencedFiles.Count);
            Assert.Contains(strings[1], info.ReferencedFiles);
            Assert.Equal(strings[1], info.ExecutableFullPath);
        }

        // -------- helpers --------

        private static byte[] BuildMinimalSccaPayload(uint version, string exeName, uint pathHash)
        {
            // Allocate a buffer at least 0xD4 bytes long so the v30 file-info parser is happy.
            byte[] data = new byte[0x140];
            BitConverter.GetBytes(version).CopyTo(data, 0x00);
            // SCCA signature
            BitConverter.GetBytes(0x41434353u).CopyTo(data, 0x04);
            // Executable name (UTF-16 LE, max 60 bytes / 30 chars)
            byte[] name = Encoding.Unicode.GetBytes(exeName);
            int nameLen = Math.Min(name.Length, 58);
            Array.Copy(name, 0, data, 0x10, nameLen);
            // Path hash
            BitConverter.GetBytes(pathHash).CopyTo(data, 0x4C);
            return data;
        }

        private static byte[] BuildStringsSection(string[] strings)
        {
            using (var ms = new MemoryStream())
            {
                foreach (var s in strings)
                {
                    byte[] bytes = Encoding.Unicode.GetBytes(s);
                    ms.Write(bytes, 0, bytes.Length);
                    ms.WriteByte(0); ms.WriteByte(0);
                }
                return ms.ToArray();
            }
        }
    }
}
