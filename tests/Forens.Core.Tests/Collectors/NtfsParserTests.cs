using System;
using System.Text;
using Forens.Core.Collectors.Ntfs;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class NtfsParserTests
    {
        [Fact]
        public void UsnJournalData_FromBytes_decodes_V0_layout()
        {
            // 56 bytes USN_JOURNAL_DATA_V0:
            //   ULONGLONG UsnJournalID(0..7), LONGLONG FirstUsn(8..15), NextUsn(16..23),
            //   LowestValidUsn(24..31), MaxUsn(32..39),
            //   DWORDLONG MaximumSize(40..47), AllocationDelta(48..55)
            byte[] data = new byte[56];
            BitConverter.GetBytes(0xCAFEBABEDEADBEEFul).CopyTo(data, 0);
            BitConverter.GetBytes(100L).CopyTo(data, 8);
            BitConverter.GetBytes(20000L).CopyTo(data, 16);
            BitConverter.GetBytes(50L).CopyTo(data, 24);
            BitConverter.GetBytes(long.MaxValue).CopyTo(data, 32);
            BitConverter.GetBytes(64ul * 1024 * 1024).CopyTo(data, 40);
            BitConverter.GetBytes(8ul * 1024 * 1024).CopyTo(data, 48);

            var info = UsnJournalData.FromBytes(data);
            Assert.Equal(0xCAFEBABEDEADBEEFul, info.UsnJournalId);
            Assert.Equal(100L, info.FirstUsn);
            Assert.Equal(20000L, info.NextUsn);
            Assert.Equal(50L, info.LowestValidUsn);
            Assert.Equal(long.MaxValue, info.MaxUsn);
            Assert.Equal(64ul * 1024 * 1024, info.MaximumSize);
            Assert.Equal(8ul * 1024 * 1024, info.AllocationDelta);
        }

        [Fact]
        public void UsnJournalData_FromBytes_rejects_short_buffer()
        {
            Assert.Throws<ArgumentException>(() => UsnJournalData.FromBytes(new byte[10]));
        }

        [Fact]
        public void UsnRecordParser_decodes_synthetic_v2_record_round_trip()
        {
            const string fileName = "test.txt";
            byte[] nameBytes = Encoding.Unicode.GetBytes(fileName);
            // Header is 60 bytes; FileName follows immediately at fileNameOffset = 60.
            int recordLen = 60 + nameBytes.Length;
            byte[] data = new byte[recordLen + 4];

            int o = 0;
            BitConverter.GetBytes((uint)recordLen).CopyTo(data, o + 0);
            BitConverter.GetBytes((ushort)2).CopyTo(data, o + 4);    // major version
            BitConverter.GetBytes((ushort)0).CopyTo(data, o + 6);    // minor version
            BitConverter.GetBytes(0x1122334455667788ul).CopyTo(data, o + 8);   // FRN
            BitConverter.GetBytes(0xAABBCCDDEEFF1122ul).CopyTo(data, o + 16);  // ParentFRN
            BitConverter.GetBytes(987654321L).CopyTo(data, o + 24);            // USN
            long ft = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(ft).CopyTo(data, o + 32);
            // Reason: FileCreate (0x100) | DataExtend (0x2) | Close (0x80000000)
            BitConverter.GetBytes(0x80000102u).CopyTo(data, o + 40);
            BitConverter.GetBytes(0u).CopyTo(data, o + 44);   // SourceInfo
            BitConverter.GetBytes(0u).CopyTo(data, o + 48);   // SecurityId
            // FileAttributes: Archive (0x20) | Directory(0x10)
            BitConverter.GetBytes(0x30u).CopyTo(data, o + 52);
            BitConverter.GetBytes((ushort)nameBytes.Length).CopyTo(data, o + 56);
            BitConverter.GetBytes((ushort)60).CopyTo(data, o + 58);
            nameBytes.CopyTo(data, o + 60);

            var rec = UsnRecordParser.Parse(data, 0);
            Assert.Equal((uint)recordLen, rec.RecordLength);
            Assert.Equal((ushort)2, rec.MajorVersion);
            Assert.Equal(0x1122334455667788ul, rec.FileReferenceNumber);
            Assert.Equal(0xAABBCCDDEEFF1122ul, rec.ParentFileReferenceNumber);
            Assert.Equal(987654321L, rec.Usn);
            Assert.NotNull(rec.TimeStampUtc);
            Assert.Equal(2026, rec.TimeStampUtc.Value.Year);
            Assert.Equal(3, rec.TimeStampUtc.Value.Month);
            Assert.Equal(0x80000102u, rec.Reason);
            Assert.Equal(fileName, rec.FileName);

            // Reason bits decoded
            Assert.Contains("FileCreate", rec.ReasonDecoded);
            Assert.Contains("DataExtend", rec.ReasonDecoded);
            Assert.Contains("Close", rec.ReasonDecoded);

            // Attribute bits decoded
            Assert.Contains("Directory", rec.FileAttributesDecoded);
            Assert.Contains("Archive", rec.FileAttributesDecoded);
        }

        [Theory]
        [InlineData(0x00000001u, "DataOverwrite")]
        [InlineData(0x00000200u, "FileDelete")]
        [InlineData(0x00002000u, "RenameNewName")]
        [InlineData(0x80000000u, "Close")]
        public void DecodeReason_includes_known_bits(uint flag, string expected)
        {
            string s = UsnRecordParser.DecodeReason(flag);
            Assert.Equal(expected, s);
        }

        [Theory]
        [InlineData(0x00000010u, "Directory")]
        [InlineData(0x00000020u, "Archive")]
        [InlineData(0x00000800u, "Compressed")]
        [InlineData(0x00004000u, "Encrypted")]
        public void DecodeFileAttributes_includes_known_bits(uint flag, string expected)
        {
            string s = UsnRecordParser.DecodeFileAttributes(flag);
            Assert.Equal(expected, s);
        }

        [Fact]
        public void DecodeReason_combines_multiple_bits_with_pipe()
        {
            string s = UsnRecordParser.DecodeReason(0x102u); // FileCreate | DataExtend
            Assert.Equal("DataExtend|FileCreate", s);
        }

        [Fact]
        public void DecodeReason_returns_empty_for_zero()
        {
            Assert.Equal("", UsnRecordParser.DecodeReason(0));
        }
    }
}
