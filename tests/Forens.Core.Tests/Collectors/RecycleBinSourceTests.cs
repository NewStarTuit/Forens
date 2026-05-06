using System;
using System.IO;
using System.Text;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class RecycleBinSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new RecycleBinSource();
            Assert.Equal("recycle-bin", src.Metadata.Id);
            Assert.Equal(Forens.Core.Collection.Category.Filesystem, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
        }

        [Fact]
        public void ParseIndex_handles_v2_format_with_filename_length_prefix()
        {
            string sampleName = @"C:\Users\test\Pictures\hello.png";
            byte[] nameBytes = Encoding.Unicode.GetBytes(sampleName);
            byte[] data = new byte[28 + nameBytes.Length];
            // version 2
            BitConverter.GetBytes((long)2).CopyTo(data, 0);
            // size = 12345
            BitConverter.GetBytes((long)12345).CopyTo(data, 8);
            // FILETIME for 2026-05-06T04:00:00Z
            long ft = new DateTime(2026, 5, 6, 4, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(ft).CopyTo(data, 16);
            // filename char count
            BitConverter.GetBytes((int)sampleName.Length).CopyTo(data, 24);
            // filename bytes
            nameBytes.CopyTo(data, 28);

            string tmp = Path.Combine(Path.GetTempPath(), "$IFAKE-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tmp, data);
            try
            {
                var record = RecycleBinSource.ParseIndex(tmp, "S-1-5-21-test");
                Assert.NotNull(record);
                Assert.Equal(2, (int)record.Version);
                Assert.Equal(12345, record.SizeBytes);
                Assert.Equal(sampleName, record.OriginalPath);
                Assert.NotNull(record.DeletedUtc);
                Assert.Equal(2026, record.DeletedUtc.Value.Year);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Fact]
        public void ParseIndex_returns_null_for_truncated_file()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "$IFAKE-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(tmp, new byte[10]);
            try
            {
                Assert.Null(RecycleBinSource.ParseIndex(tmp, "S-1-5-21-test"));
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Fact]
        public void SafeFileTime_returns_null_for_zero_or_negative()
        {
            Assert.Null(RecycleBinSource.SafeFileTime(0));
            Assert.Null(RecycleBinSource.SafeFileTime(-1));
        }
    }
}
