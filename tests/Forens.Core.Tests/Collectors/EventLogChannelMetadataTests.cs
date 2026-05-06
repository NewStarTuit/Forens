using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class EventLogChannelMetadataTests
    {
        [Fact]
        public void EventLogSystemSource_metadata()
        {
            var src = new EventLogSystemSource();
            Assert.Equal("eventlog-system", src.Metadata.Id);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsTimeRange);
            Assert.Contains(ContendedResource.EventLogSystem, src.Metadata.ContendedResources);
        }

        [Fact]
        public void EventLogSecuritySource_metadata_requires_elevation()
        {
            var src = new EventLogSecuritySource();
            Assert.Equal("eventlog-security", src.Metadata.Id);
            Assert.True(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsTimeRange);
            Assert.Contains(ContendedResource.EventLogSecurity, src.Metadata.ContendedResources);
        }

        [Fact]
        public void EventLogSecuritySource_skips_when_unprivileged()
        {
            var src = new EventLogSecuritySource();
            var ctx = TestContexts.Build(elevation: ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.SkipRequiresElevation, pre.Result);
            Assert.Contains("administrator", pre.Reason, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EventLogSecuritySource_proceeds_when_elevated()
        {
            var src = new EventLogSecuritySource();
            var ctx = TestContexts.Build(elevation: ElevationState.Elevated);
            Assert.Equal(PreconditionResult.Ok, src.CheckPrecondition(ctx).Result);
        }

        [Fact]
        public void EventLogDefenderSource_metadata()
        {
            var src = new EventLogDefenderSource();
            Assert.Equal("eventlog-defender", src.Metadata.Id);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsTimeRange);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
        }
    }

    internal static class TestContexts
    {
        public static CollectionContext Build(ElevationState elevation)
        {
            return new CollectionContext(
                runId: System.Guid.Empty,
                outputDir: System.IO.Path.GetTempPath(),
                timeFrom: null, timeTo: null,
                processFilter: ProcessFilter.Empty,
                elevation: elevation,
                hostOsVersion: new System.Version(10, 0),
                cancellationToken: System.Threading.CancellationToken.None,
                logger: new Serilog.LoggerConfiguration().CreateLogger());
        }
    }
}
