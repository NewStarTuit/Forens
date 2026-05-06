using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class LnkTargetsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new LnkTargetsSource();
            Assert.Equal("lnk-targets", src.Metadata.Id);
            Assert.Equal(Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.HistoricalImagePath, src.Metadata.ProcessFilterMode);
        }
    }
}
