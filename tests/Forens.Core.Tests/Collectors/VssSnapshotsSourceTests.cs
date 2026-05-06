using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class VssSnapshotsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new VssSnapshotsSource();
            Assert.Equal("vss-snapshots", src.Metadata.Id);
            Assert.Equal(Category.Filesystem, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.Contains(ContendedResource.WmiCimV2, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_returns_Ok()
        {
            var src = new VssSnapshotsSource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            Assert.Equal(PreconditionResult.Ok, src.CheckPrecondition(ctx).Result);
        }
    }
}
