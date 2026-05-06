using System;
using System.Collections.Generic;
using System.IO;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collection.Run;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;

namespace Forens.Core.Tests.Collection
{
    public class ManifestBuilderTests
    {
        [Fact]
        public void Produced_manifest_validates_against_schema()
        {
            var run = BuildSampleRun();
            string json = ManifestBuilder.SerializeToJson(run);
            var doc = JObject.Parse(json);

            string schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "manifest-schema.json");
            Assert.True(File.Exists(schemaPath), "schema not copied to output: " + schemaPath);
            var schema = JSchema.Parse(File.ReadAllText(schemaPath));
            IList<string> errors;
            bool valid = doc.IsValid(schema, out errors);
            Assert.True(valid, "Manifest does not validate. Errors:\n  " + string.Join("\n  ", errors));
        }

        [Fact]
        public void Required_top_level_fields_present()
        {
            var run = BuildSampleRun();
            var doc = JObject.Parse(ManifestBuilder.SerializeToJson(run));
            Assert.Equal("forens-manifest", (string)doc["schema"]?["name"]);
            Assert.Equal("1.0.0", (string)doc["schema"]?["version"]);
            Assert.NotNull(doc["runId"]);
            Assert.NotNull(doc["tool"]);
            Assert.Equal("net462", (string)doc["tool"]?["targetFramework"]);
            Assert.NotNull(doc["host"]);
            Assert.NotNull(doc["operator"]);
            Assert.NotNull(doc["request"]);
            Assert.NotNull(doc["results"]);
        }

        [Fact]
        public void Per_file_sha256_is_64_char_lowercase_hex()
        {
            var run = BuildSampleRun();
            var doc = JObject.Parse(ManifestBuilder.SerializeToJson(run));
            var firstResult = doc["results"]?[0];
            Assert.NotNull(firstResult);
            var firstFile = firstResult["outputFiles"]?[0];
            Assert.NotNull(firstFile);
            string sha = (string)firstFile["sha256"];
            Assert.Matches("^[0-9a-f]{64}$", sha);
        }

        [Fact]
        public void Skipped_status_records_status_reason_and_omits_items_bytes()
        {
            var skipped = new SourceResult(
                "skipped-source", SourceStatus.Skipped, "requires elevation",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0,
                Array.Empty<OutputFile>(), null);
            var run = BuildRunWithResults(new[] { skipped });

            var doc = JObject.Parse(ManifestBuilder.SerializeToJson(run));
            var r = doc["results"]?[0];
            Assert.Equal("Skipped", (string)r["status"]);
            Assert.Equal("requires elevation", (string)r["statusReason"]);
            Assert.Null(r["itemsCollected"]);
            Assert.Null(r["bytesWritten"]);
        }

        private static CollectionRun BuildSampleRun()
        {
            var of = new OutputFile(
                "raw/test-source-a/data.jsonl",
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                42, DateTimeOffset.UtcNow);
            var sr = new SourceResult(
                "test-source-a", SourceStatus.Succeeded, null,
                DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow,
                3, 42, new[] { of }, null);
            return BuildRunWithResults(new[] { sr });
        }

        private static CollectionRun BuildRunWithResults(IReadOnlyList<SourceResult> results)
        {
            return new CollectionRun(
                runId: Guid.NewGuid(),
                toolVersion: "0.1.0",
                gitCommit: "abc1234",
                hostName: "TEST-HOST",
                hostOsVersion: "Windows 10.0.19045",
                operatorAccount: @"TEST-HOST\op",
                elevation: ElevationState.NotElevated,
                caseId: "INC-1",
                profileName: "default",
                requestedSources: new[] { "test-source-a" },
                timeFrom: null, timeTo: null,
                processFilter: null,
                cli: new[] { "forens", "collect", "--sources", "test-source-a" },
                outputRoot: @"C:\out\forens-TEST-HOST-2026-05-06T14-22-33Z",
                startedUtc: DateTimeOffset.UtcNow.AddSeconds(-2),
                completedUtc: DateTimeOffset.UtcNow,
                status: RunStatus.Completed,
                statusReason: null,
                results: results);
        }
    }
}
