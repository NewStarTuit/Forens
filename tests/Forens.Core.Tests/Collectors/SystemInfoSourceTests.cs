using System;
using System.IO;
using System.Linq;
using System.Threading;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Newtonsoft.Json.Linq;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class SystemInfoSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new SystemInfoSource();
            Assert.Equal("system-info", src.Metadata.Id);
            Assert.Equal(Category.System, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
        }

        [Fact]
        public void Output_is_a_single_record_with_expected_fields()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-sysinfo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new SystemInfoSource();
                using (var w = new StreamingOutputWriter(dir, "raw/system-info"))
                {
                    src.Collect(Build(dir), w);
                    Assert.Equal(1, w.ItemsCollected);
                }
                var lines = File.ReadAllLines(Path.Combine(dir, "system-info.jsonl"));
                Assert.Single(lines);
                var rec = JObject.Parse(lines[0]);
                Assert.NotNull(rec["machineName"]);
                Assert.NotNull(rec["osVersion"]);
                Assert.NotNull(rec["timeZoneId"]);
                Assert.NotNull(rec["processorCount"]);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Theory]
        [InlineData("20260226225511.000000+480", 2026, 2, 26)]
        [InlineData("20260101000000.000000+000", 2026, 1, 1)]
        public void ParseWmiDateTime_handles_CIM_datetime_format(string wmi, int year, int month, int day)
        {
            var dto = SystemInfoSource.ParseWmiDateTime(wmi);
            Assert.NotNull(dto);
            Assert.Equal(year, dto.Value.Year);
            Assert.Equal(month, dto.Value.Month);
            Assert.Equal(day, dto.Value.Day);
            Assert.Equal(TimeSpan.Zero, dto.Value.Offset);
        }

        [Fact]
        public void ParseWmiDateTime_returns_null_on_garbage()
        {
            Assert.Null(SystemInfoSource.ParseWmiDateTime(""));
            Assert.Null(SystemInfoSource.ParseWmiDateTime("not a date"));
            Assert.Null(SystemInfoSource.ParseWmiDateTime(null));
        }

        private static CollectionContext Build(string outputDir)
        {
            return new CollectionContext(
                runId: Guid.Empty,
                outputDir: outputDir,
                timeFrom: null, timeTo: null,
                processFilter: ProcessFilter.Empty,
                elevation: ElevationState.NotElevated,
                hostOsVersion: new Version(10, 0),
                cancellationToken: CancellationToken.None,
                logger: new LoggerConfiguration().CreateLogger());
        }
    }
}
