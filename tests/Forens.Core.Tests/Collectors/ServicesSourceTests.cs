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
    public class ServicesSourceTests
    {
        [Fact]
        public void Metadata_id_is_kebab_case_and_declares_expected_capabilities()
        {
            var src = new ServicesSource();
            Assert.Equal("services", src.Metadata.Id);
            Assert.Matches("^[a-z][a-z0-9-]*$", src.Metadata.Id);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.LivePid, src.Metadata.ProcessFilterMode);
            Assert.Contains(ContendedResource.WmiCimV2, src.Metadata.ContendedResources);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
        }

        [Fact]
        public void Precondition_is_Ok_unprivileged()
        {
            Assert.Equal(PreconditionResult.Ok, new ServicesSource().CheckPrecondition(Build()).Result);
        }

        [Fact]
        public void Output_records_have_required_fields()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-services-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new ServicesSource();
                using (var w = new StreamingOutputWriter(dir, "raw/services"))
                {
                    src.Collect(Build(dir), w);
                    Assert.True(w.ItemsCollected > 0, "expected at least one service");
                }
                var first = JObject.Parse(File.ReadAllLines(Path.Combine(dir, "services.jsonl")).First());
                Assert.NotNull(first["name"]);
                Assert.NotNull(first["displayName"]);
                Assert.NotNull(first["state"]);
                Assert.NotNull(first["startMode"]);
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
