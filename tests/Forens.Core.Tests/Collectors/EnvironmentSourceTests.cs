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
    public class EnvironmentSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new EnvironmentSource();
            Assert.Equal("environment", src.Metadata.Id);
            Assert.Equal(Category.System, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
        }

        [Fact]
        public void Output_includes_machine_user_and_process_scopes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-env-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new EnvironmentSource();
                using (var w = new StreamingOutputWriter(dir, "raw/environment"))
                {
                    src.Collect(Build(dir), w);
                    Assert.True(w.ItemsCollected > 0);
                }
                var lines = File.ReadAllLines(Path.Combine(dir, "environment.jsonl"));
                var scopes = lines.Select(l => (string)JObject.Parse(l)["scope"]).Distinct().ToArray();
                Assert.Contains("Machine", scopes);
                Assert.Contains("Process", scopes);
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
