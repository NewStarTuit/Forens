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
    public class NetworkConfigSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new NetworkConfigSource();
            Assert.Equal("network-config", src.Metadata.Id);
            Assert.Equal(Category.Network, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
        }

        [Fact]
        public void FormatMac_produces_dash_separated_uppercase_hex()
        {
            byte[] bytes = { 0x00, 0x50, 0x56, 0xC0, 0x00, 0x01 };
            Assert.Equal("00-50-56-C0-00-01", NetworkConfigSource.FormatMac(bytes));
        }

        [Fact]
        public void FormatMac_returns_null_for_empty_or_null_input()
        {
            Assert.Null(NetworkConfigSource.FormatMac(null));
            Assert.Null(NetworkConfigSource.FormatMac(new byte[0]));
        }

        [Fact]
        public void Live_collect_against_this_host_produces_at_least_one_interface()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-nc-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new NetworkConfigSource();
                using (var w = new StreamingOutputWriter(dir, "raw/network-config"))
                {
                    src.Collect(Build(dir), w);
                    Assert.True(w.ItemsCollected > 0);
                }
                var lines = File.ReadAllLines(Path.Combine(dir, "interfaces.jsonl"));
                Assert.NotEmpty(lines);
                var first = JObject.Parse(lines[0]);
                Assert.NotNull(first["id"]);
                Assert.NotNull(first["interfaceType"]);
                Assert.NotNull(first["operationalStatus"]);
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
