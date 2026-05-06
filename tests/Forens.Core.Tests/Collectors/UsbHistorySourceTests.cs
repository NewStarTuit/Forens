using System;
using System.IO;
using System.Threading;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Newtonsoft.Json.Linq;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class UsbHistorySourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new UsbHistorySource();
            Assert.Equal("usb-history", src.Metadata.Id);
            Assert.Equal(Category.System, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.RegistryHiveSystem, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Live_collect_against_this_host_produces_records_or_empty_file()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-usb-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new UsbHistorySource();
                using (var w = new StreamingOutputWriter(dir, "raw/usb-history"))
                {
                    src.Collect(Build(dir), w);
                }
                // File must always exist; record count depends on whether USB devices were ever attached.
                Assert.True(File.Exists(Path.Combine(dir, "usb-history.jsonl")));
                var lines = File.ReadAllLines(Path.Combine(dir, "usb-history.jsonl"));
                if (lines.Length > 0)
                {
                    var first = JObject.Parse(lines[0]);
                    Assert.NotNull(first["sourceCategory"]);
                    Assert.NotNull(first["deviceClass"]);
                    Assert.NotNull(first["instanceId"]);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
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
