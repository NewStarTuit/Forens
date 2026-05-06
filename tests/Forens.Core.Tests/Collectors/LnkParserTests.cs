using System;
using System.IO;
using System.Text;
using Forens.Core.Collectors.Lnk;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class LnkParserTests
    {
        [Fact]
        public void Parse_returns_error_for_too_short_buffer()
        {
            var info = LnkParser.Parse(new byte[10]);
            Assert.False(info.HeaderValid);
            Assert.Contains("ShellLinkHeader", info.ParseError);
        }

        [Fact]
        public void Parse_rejects_wrong_HeaderSize()
        {
            byte[] data = new byte[0x4C];
            // wrong header size
            BitConverter.GetBytes(0x42u).CopyTo(data, 0);
            var info = LnkParser.Parse(data);
            Assert.False(info.HeaderValid);
            Assert.Contains("HeaderSize", info.ParseError);
        }

        [Fact]
        public void Parse_rejects_wrong_LinkCLSID()
        {
            byte[] data = new byte[0x4C];
            BitConverter.GetBytes(0x4Cu).CopyTo(data, 0);
            // CLSID bytes left zero — should not match the SHLLINK CLSID
            var info = LnkParser.Parse(data);
            Assert.False(info.HeaderValid);
            Assert.Contains("CLSID", info.ParseError);
        }

        [Fact]
        public void Parse_extracts_header_fields_from_synthetic_lnk_with_valid_clsid()
        {
            byte[] data = BuildMinimalLnkHeader(linkFlags: 0x80, fileAttributes: 0x20, fileSize: 0x1000);
            // Add target FILETIMEs
            long ftCreate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(ftCreate).CopyTo(data, 0x1C);

            var info = LnkParser.Parse(data);
            Assert.True(info.HeaderValid);
            Assert.Equal(0x80u, info.LinkFlags);
            Assert.Equal(0x20u, info.FileAttributes);
            Assert.Equal(0x1000u, info.TargetFileSize);
            Assert.NotNull(info.TargetCreationUtc);
            Assert.Equal(2026, info.TargetCreationUtc.Value.Year);
        }

        [Theory]
        [InlineData(0u, "DRIVE_UNKNOWN")]
        [InlineData(2u, "DRIVE_REMOVABLE")]
        [InlineData(3u, "DRIVE_FIXED")]
        [InlineData(4u, "DRIVE_REMOTE")]
        [InlineData(5u, "DRIVE_CDROM")]
        [InlineData(99u, "Unknown(99)")]
        public void DriveTypeName_decodes_known_constants(uint code, string expected)
        {
            Assert.Equal(expected, LnkParser.DriveTypeName(code));
        }

        [Fact]
        public void ReadStringData_unicode_path_round_trip()
        {
            string s = "C:\\Program Files\\Test.exe";
            byte[] sBytes = Encoding.Unicode.GetBytes(s);
            byte[] data = new byte[2 + sBytes.Length];
            BitConverter.GetBytes((ushort)s.Length).CopyTo(data, 0);
            sBytes.CopyTo(data, 2);

            int pos = 0;
            string read = LnkParser.ReadStringData(data, ref pos, isUnicode: true, ok: out bool ok);
            Assert.True(ok);
            Assert.Equal(s, read);
            Assert.Equal(2 + sBytes.Length, pos);
        }

        [Fact]
        public void ReadCStringUnicode_stops_at_double_null_terminator()
        {
            string s = "Hello";
            byte[] body = Encoding.Unicode.GetBytes(s);
            byte[] data = new byte[body.Length + 4];
            body.CopyTo(data, 0);
            // double null at body.Length
            // remaining bytes are noise
            data[body.Length + 2] = 0xDE;
            data[body.Length + 3] = 0xAD;

            string read = LnkParser.ReadCStringUnicode(data, 0, data.Length);
            Assert.Equal(s, read);
        }

        // --- helpers ---

        private static readonly byte[] LinkCLSID =
        {
            0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
        };

        private static byte[] BuildMinimalLnkHeader(uint linkFlags, uint fileAttributes, uint fileSize)
        {
            byte[] data = new byte[0x4C];
            BitConverter.GetBytes(0x4Cu).CopyTo(data, 0);
            Array.Copy(LinkCLSID, 0, data, 0x04, 16);
            BitConverter.GetBytes(linkFlags).CopyTo(data, 0x14);
            BitConverter.GetBytes(fileAttributes).CopyTo(data, 0x18);
            BitConverter.GetBytes(fileSize).CopyTo(data, 0x34);
            return data;
        }
    }
}
