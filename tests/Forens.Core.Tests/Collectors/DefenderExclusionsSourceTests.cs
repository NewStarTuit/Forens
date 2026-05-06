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
    public class DefenderExclusionsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new DefenderExclusionsSource();
            Assert.Equal("defender-exclusions", src.Metadata.Id);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsProcessFilter);
        }

        [Fact]
        public void Precondition_returns_Ok_unprivileged()
        {
            var src = new DefenderExclusionsSource();
            var ctx = Build();
            Assert.Equal(PreconditionResult.Ok, src.CheckPrecondition(ctx).Result);
        }

        [Fact]
        public void Live_collect_handles_access_denied_root_keys_cleanly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-defex-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new DefenderExclusionsSource();
                using (var w = new StreamingOutputWriter(dir, "raw/defender-exclusions"))
                {
                    // Should NEVER throw — if both Defender roots are access-denied (typical on
                    // Win 11 non-admin), the source records Partial with a clear reason and
                    // emits whatever it could enumerate from the policy root.
                    src.Collect(Build(dir), w);
                    Assert.True(File.Exists(Path.Combine(dir, "defender-exclusions.jsonl")));
                }
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
