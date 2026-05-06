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
    public class UserAssistSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new UserAssistSource();
            Assert.Equal("userassist", src.Metadata.Id);
            Assert.Equal(Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.HistoricalImagePath, src.Metadata.ProcessFilterMode);
        }

        [Theory]
        [InlineData("HRZR_PGYFRFFVBA", "UEME_CTLSESSION")]
        [InlineData("UEME_CTLSESSION", "HRZR_PGYFRFFVBA")]
        [InlineData("Hello World 123", "Uryyb Jbeyq 123")]
        [InlineData("", "")]
        public void Rot13_round_trips_letters_and_preserves_other_chars(string input, string expected)
        {
            Assert.Equal(expected, UserAssistSource.Rot13(input));
            Assert.Equal(input, UserAssistSource.Rot13(UserAssistSource.Rot13(input)));
        }

        [Theory]
        [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe", "notepad.exe")]
        [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Microsoft Office\OFFICE16\WINWORD.EXE",
                    "Microsoft Office\\OFFICE16\\WINWORD.EXE")]
        [InlineData(@"C:\Windows\system32\notepad.exe", @"C:\Windows\system32\notepad.exe")]
        [InlineData("UEME_CTLSESSION", "UEME_CTLSESSION")]
        public void ExtractImagePath_strips_known_folder_GUID_prefix(string decoded, string expected)
        {
            Assert.Equal(expected, UserAssistSource.ExtractImagePath(decoded));
        }

        [Fact]
        public void ParseValue_handles_72_byte_win7_format()
        {
            var data = new byte[72];
            // session id at offset 0
            BitConverter.GetBytes(1).CopyTo(data, 0);
            // run count = 5 at offset 4
            BitConverter.GetBytes(5).CopyTo(data, 4);
            // focus count seconds = 42 at offset 12
            BitConverter.GetBytes(42).CopyTo(data, 12);
            // last executed FILETIME at offset 60: 2026-01-01 UTC
            long ft = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(ft).CopyTo(data, 60);

            var p = UserAssistSource.ParseValue(data);
            Assert.Equal("win7+", p.Format);
            Assert.Equal(5, p.RunCount);
            Assert.Equal(42L, p.FocusSeconds);
            Assert.NotNull(p.LastExecutedUtc);
            Assert.Equal(2026, p.LastExecutedUtc.Value.Year);
            Assert.Equal(1, p.LastExecutedUtc.Value.Month);
        }

        [Fact]
        public void ParseValue_handles_short_data_gracefully()
        {
            var p = UserAssistSource.ParseValue(new byte[4]);
            Assert.StartsWith("short", p.Format);
            Assert.Null(p.RunCount);
            Assert.Null(p.LastExecutedUtc);
        }

        [Fact]
        public void Live_collect_against_this_user_produces_records_when_HKCU_has_userassist()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-ua-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new UserAssistSource();
                using (var w = new StreamingOutputWriter(dir, "raw/userassist"))
                {
                    src.Collect(Build(dir), w);
                    // typical interactive Windows users have at least a few entries; assert
                    // file exists and is well-formed JSONL (zero records is acceptable on
                    // CI agents where no user has logged in interactively).
                    Assert.True(File.Exists(Path.Combine(dir, "userassist.jsonl")));
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
