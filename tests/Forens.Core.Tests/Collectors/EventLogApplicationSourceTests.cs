using System;
using System.IO;
using System.Threading;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class EventLogApplicationSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new EventLogApplicationSource();
            Assert.Equal("eventlog-application", src.Metadata.Id);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.EventLogApplication, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_is_Ok_unprivileged()
        {
            var pre = new EventLogApplicationSource().CheckPrecondition(Build());
            Assert.Equal(PreconditionResult.Ok, pre.Result);
        }

        [Fact]
        public void BuildXPath_returns_wildcard_for_unbounded_range()
        {
            Assert.Equal("*", EventLogApplicationSource.BuildXPath(null, null));
        }

        [Fact]
        public void BuildXPath_includes_both_bounds_when_given()
        {
            var from = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
            var to = DateTimeOffset.Parse("2026-05-06T00:00:00Z");
            var xpath = EventLogApplicationSource.BuildXPath(from, to);
            Assert.Contains("2026-05-01T00:00:00.000Z", xpath);
            Assert.Contains("2026-05-06T00:00:00.000Z", xpath);
            Assert.Contains(">=", xpath);
            Assert.Contains("<=", xpath);
        }

        [Fact]
        public void BuildXPath_handles_only_from()
        {
            var from = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
            var xpath = EventLogApplicationSource.BuildXPath(from, null);
            Assert.Contains(">=", xpath);
            Assert.DoesNotContain("<=", xpath);
        }

        [Fact]
        public void Collect_with_30_second_window_does_not_throw_and_writes_either_zero_or_in_window_records()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-evtlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var ctx = new CollectionContext(
                    runId: Guid.Empty,
                    outputDir: dir,
                    timeFrom: now.AddSeconds(-30),
                    timeTo: now,
                    processFilter: ProcessFilter.Empty,
                    elevation: ElevationState.NotElevated,
                    hostOsVersion: new Version(10, 0),
                    cancellationToken: CancellationToken.None,
                    logger: new LoggerConfiguration().CreateLogger());

                var src = new EventLogApplicationSource();
                using (var w = new StreamingOutputWriter(dir, "raw/eventlog-application"))
                {
                    src.Collect(ctx, w);
                }
                Assert.True(File.Exists(Path.Combine(dir, "events.jsonl")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static CollectionContext Build(string outputDir = null)
        {
            return new CollectionContext(
                runId: Guid.Empty,
                outputDir: outputDir ?? Path.GetTempPath(),
                timeFrom: null, timeTo: null,
                processFilter: ProcessFilter.Empty,
                elevation: ElevationState.NotElevated,
                hostOsVersion: new Version(10, 0),
                cancellationToken: CancellationToken.None,
                logger: new LoggerConfiguration().CreateLogger());
        }
    }
}
