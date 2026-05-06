using System.Text;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class RecentDocsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new RecentDocsSource();
            Assert.Equal("recentdocs", src.Metadata.Id);
            Assert.Equal(Forens.Core.Collection.Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
        }

        [Fact]
        public void ExtractUnicodeFilenamePrefix_returns_filename_before_double_null()
        {
            // "report.txt" UTF-16 LE + double-null + arbitrary trailing bytes.
            var name = Encoding.Unicode.GetBytes("report.txt");
            byte[] data = new byte[name.Length + 2 + 8];
            name.CopyTo(data, 0);
            data[name.Length] = 0; data[name.Length + 1] = 0;
            // garbage shell-link bytes after the null-terminator
            data[name.Length + 2] = 0xDE;
            data[name.Length + 3] = 0xAD;

            Assert.Equal("report.txt", RecentDocsSource.ExtractUnicodeFilenamePrefix(data));
        }

        [Fact]
        public void ExtractUnicodeFilenamePrefix_returns_null_for_empty_or_no_terminator()
        {
            Assert.Null(RecentDocsSource.ExtractUnicodeFilenamePrefix(null));
            Assert.Null(RecentDocsSource.ExtractUnicodeFilenamePrefix(new byte[0]));
            // No double-null inside this 4-byte UTF-16 LE "ab" without terminator.
            Assert.Null(RecentDocsSource.ExtractUnicodeFilenamePrefix(new byte[] { 0x61, 0x00, 0x62, 0x00 }));
        }
    }
}
