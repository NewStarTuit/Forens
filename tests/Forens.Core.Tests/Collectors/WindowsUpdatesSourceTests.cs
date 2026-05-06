using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class WindowsUpdatesSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new WindowsUpdatesSource();
            Assert.Equal("windows-updates", src.Metadata.Id);
            Assert.Equal(Category.System, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.WmiCimV2, src.Metadata.ContendedResources);
        }
    }
}
