using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Forens.Core.Collection;
using Forens.Core.Collection.Run;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Forens.Core.Tests.Collection
{
    [Collection("CollectionRunnerSerial")]
    public class CollectionRunnerTests
    {
        [Fact]
        public void End_to_end_run_writes_manifest_and_per_source_raw_files()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-runner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);

            try
            {
                // Build a catalog of just our fake source via reflection over the test assembly.
                var catalog = SourceCatalog.DiscoverFromTypes(new[]
                {
                    typeof(FakeWritingSource), typeof(FakeFailingSource), typeof(FakeSecondSource)
                });
                Assert.True(catalog.Contains(FakeWritingSource.Id));

                var runner = new CollectionRunner(catalog);
                var cfg = new CollectionConfiguration
                {
                    OutputRoot = outRoot,
                    SelectedSources = new[] { FakeWritingSource.Id },
                    Parallelism = 1,
                    DiskFloorBytes = 0
                };

                var run = runner.Run(cfg);
                Assert.Equal(RunStatus.Completed, run.Status);
                Assert.Single(run.Results);
                var r = run.Results[0];
                Assert.Equal(SourceStatus.Succeeded, r.Status);
                Assert.True(r.ItemsCollected >= 3);

                Assert.True(Directory.Exists(run.OutputRoot), "run dir missing: " + run.OutputRoot);
                string manifestPath = Path.Combine(run.OutputRoot, "manifest.json");
                Assert.True(File.Exists(manifestPath));
                string rawDir = Path.Combine(run.OutputRoot, "raw", FakeWritingSource.Id);
                Assert.True(Directory.Exists(rawDir));
                Assert.True(Directory.EnumerateFiles(rawDir).Any());

                var doc = JObject.Parse(File.ReadAllText(manifestPath));
                Assert.Equal("Completed", (string)doc["status"]);
                var resultsArray = (JArray)doc["results"];
                Assert.Single(resultsArray);
                var outputFile = resultsArray[0]["outputFiles"][0];
                string declared = (string)outputFile["sha256"];
                string declaredPath = (string)outputFile["relativePath"];

                string actualPath = Path.Combine(run.OutputRoot, declaredPath.Replace('/', Path.DirectorySeparatorChar));
                using (var sha = SHA256.Create())
                {
                    string actualHex = ToHex(sha.ComputeHash(File.ReadAllBytes(actualPath)));
                    Assert.Equal(declared, actualHex);
                }
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        [Fact]
        public void Source_failure_does_not_abort_siblings()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-runner-fail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var catalog = SourceCatalog.DiscoverFromTypes(new[]
                {
                    typeof(FakeWritingSource), typeof(FakeFailingSource), typeof(FakeSecondSource)
                });
                var runner = new CollectionRunner(catalog);
                var cfg = new CollectionConfiguration
                {
                    OutputRoot = outRoot,
                    SelectedSources = new[] { FakeWritingSource.Id, FakeFailingSource.Id, FakeSecondSource.Id },
                    Parallelism = 2,
                    DiskFloorBytes = 0
                };
                var run = runner.Run(cfg);
                Assert.Equal(RunStatus.CompletedWithErrors, run.Status);
                Assert.Equal(3, run.Results.Count);
                Assert.Contains(run.Results, r => r.SourceId == FakeWritingSource.Id && r.Status == SourceStatus.Succeeded);
                Assert.Contains(run.Results, r => r.SourceId == FakeFailingSource.Id && r.Status == SourceStatus.Failed);
                Assert.Contains(run.Results, r => r.SourceId == FakeSecondSource.Id && r.Status == SourceStatus.Succeeded);
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        public sealed class FakeWritingSource : IArtifactSource
        {
            public const string Id = "fake-writing-source";
            public SourceMetadata Metadata { get; } = new SourceMetadata(
                Id, "Fake Writing", "Writes a few JSONL records.",
                Category.System, false, false, false, ProcessFilterMode.None,
                Array.Empty<ContendedResource>(), 8, null);
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer)
            {
                using (var jl = writer.OpenJsonlFile("data.jsonl"))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        jl.Write(new { index = i, text = "row " + i });
                        writer.RecordItem();
                    }
                }
            }
        }

        public sealed class FakeFailingSource : IArtifactSource
        {
            public const string Id = "fake-failing-source";
            public SourceMetadata Metadata { get; } = new SourceMetadata(
                Id, "Fake Failing", "Throws on collect.",
                Category.System, false, false, false, ProcessFilterMode.None,
                Array.Empty<ContendedResource>(), 8, null);
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer)
            {
                throw new InvalidOperationException("simulated source failure");
            }
        }

        public sealed class FakeSecondSource : IArtifactSource
        {
            public const string Id = "fake-second-source";
            public SourceMetadata Metadata { get; } = new SourceMetadata(
                Id, "Fake Second", "Writes one record.",
                Category.System, false, false, false, ProcessFilterMode.None,
                Array.Empty<ContendedResource>(), 8, null);
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer)
            {
                using (var jl = writer.OpenJsonlFile("one.jsonl"))
                {
                    jl.Write(new { hello = "world" });
                    writer.RecordItem();
                }
            }
        }
    }
}
