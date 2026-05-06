using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class WmiStartupCommandsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new WmiStartupCommandsSource();
            Assert.Equal("wmi-startup-commands", src.Metadata.Id);
            Assert.Equal(Forens.Core.Collection.Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsProcessFilter);
        }

        [Theory]
        [InlineData("\"C:\\App\\app.exe\" /background", "C:\\App\\app.exe")]
        [InlineData("C:\\Windows\\notepad.exe", "C:\\Windows\\notepad.exe")]
        [InlineData("notepad.exe", "notepad.exe")]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ParseImagePath_handles_quoted_unquoted_and_empty(string input, string expected)
        {
            Assert.Equal(expected, WmiStartupCommandsSource.ParseImagePath(input));
        }
    }
}
