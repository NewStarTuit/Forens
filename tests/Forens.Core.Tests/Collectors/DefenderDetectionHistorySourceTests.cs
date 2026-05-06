using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class DefenderDetectionHistorySourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new DefenderDetectionHistorySource();
            Assert.Equal("defender-detection-history", src.Metadata.Id);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
        }

        [Fact]
        public void Precondition_returns_definitive_decision()
        {
            var src = new DefenderDetectionHistorySource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.NotNull(pre);
            Assert.True(
                pre.Result == PreconditionResult.Ok ||
                pre.Result == PreconditionResult.SkipRequiresElevation ||
                pre.Result == PreconditionResult.SkipNotAvailableOnHost,
                "Unexpected precondition result: " + pre.Result);
        }
    }
}
