using System;
using System.IO;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class PrefetchSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new PrefetchSource();
            Assert.Equal("prefetch", src.Metadata.Id);
            Assert.Equal(Forens.Core.Collection.Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsProcessFilter);
        }

        [Fact]
        public void BuildRecord_falls_back_to_filename_when_payload_unparseable()
        {
            // 8 zero bytes — declares it's MAM-compressed (header byte mismatch),
            // so parser will fail and the record should still emit filename-derived data.
            string tmp = Path.Combine(Path.GetTempPath(), "NOTEPAD.EXE-DEADBEEF.pf");
            File.WriteAllBytes(tmp, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            try
            {
                var rec = PrefetchSource.BuildRecord(tmp);
                Assert.NotNull(rec);
                Assert.Equal("NOTEPAD.EXE", rec.ExecutableName);
                Assert.Equal("DEADBEEF", rec.PathHash);
                Assert.True(rec.SizeBytes >= 8);
                Assert.False(string.IsNullOrEmpty(rec.ParseError));
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Fact]
        public void BuildRecord_returns_record_even_when_filename_pattern_does_not_match()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "NOT-A-PREFETCH-NAME.pf");
            File.WriteAllBytes(tmp, new byte[] { 0x00, 0x00, 0x00, 0x00 });
            try
            {
                var rec = PrefetchSource.BuildRecord(tmp);
                Assert.NotNull(rec);
                Assert.Null(rec.ExecutableName);
                Assert.Null(rec.PathHash);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
