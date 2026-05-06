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
    public class InstalledSoftwareSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new InstalledSoftwareSource();
            Assert.Equal("installed-software", src.Metadata.Id);
            Assert.Equal(Category.System, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
        }

        [Fact]
        public void Output_records_have_required_fields_when_present()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-isw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new InstalledSoftwareSource();
                using (var w = new StreamingOutputWriter(dir, "raw/installed-software"))
                {
                    src.Collect(Build(dir), w);
                    Assert.True(w.ItemsCollected > 0, "expected at least one installed program on a typical Windows host");
                }
                var first = JObject.Parse(File.ReadAllLines(Path.Combine(dir, "installed-software.jsonl")).First());
                Assert.NotNull(first["displayName"]);
                Assert.False(string.IsNullOrEmpty((string)first["displayName"]));
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
