using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class AmcacheSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new AmcacheSource();
            Assert.Equal("amcache", src.Metadata.Id);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.HistoricalImagePath, src.Metadata.ProcessFilterMode);
        }

        [Fact]
        public void Precondition_returns_definitive_decision()
        {
            // On any Windows host, the precondition either Ok's (Amcache.hve accessible),
            // SkipRequiresElevation (file present but ACL'd), or SkipNotAvailableOnHost
            // (very old Windows). It MUST never throw.
            var src = new AmcacheSource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.NotNull(pre);
            Assert.True(
                pre.Result == PreconditionResult.Ok ||
                pre.Result == PreconditionResult.SkipRequiresElevation ||
                pre.Result == PreconditionResult.SkipNotAvailableOnHost,
                "Unexpected precondition result: " + pre.Result);
            if (pre.Result != PreconditionResult.Ok)
            {
                Assert.False(string.IsNullOrEmpty(pre.Reason));
            }
        }
    }
}
