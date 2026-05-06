using Xunit;

namespace Forens.Integration.Tests.Cli
{
    public class CollectCommandValidationTests
    {
        [Fact]
        public void Collect_with_no_sources_or_profile_defaults_to_live_triage()
        {
            // `forens collect --dry-run` with no --sources / --profile should NOT exit 2;
            // it should default to live-triage, print an explanatory note, and complete dry-run.
            var r = CliRunner.Run("collect", "--dry-run", "--output", System.IO.Path.GetTempPath());
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("defaulting to --profile live-triage", r.StdOut);
            Assert.Contains("Profile: live-triage", r.StdOut);
        }

        [Fact]
        public void Unknown_source_id_exits_2_with_valid_ids_in_stderr()
        {
            var r = CliRunner.Run("collect", "--sources", "definitely-not-a-real-source");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("Unknown source id", r.StdErr);
            Assert.Contains("Valid ids", r.StdErr);
            Assert.Contains("process-list", r.StdErr);
        }

        [Fact]
        public void Profile_flag_is_accepted_and_resolves_to_source_list()
        {
            var r = CliRunner.Run("collect", "--profile", "live-triage", "--dry-run", "--output", System.IO.Path.GetTempPath());
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("Profile: live-triage", r.StdOut);
        }

        [Fact]
        public void Unknown_profile_is_rejected_with_valid_profiles_listed()
        {
            var r = CliRunner.Run("collect", "--profile", "definitely-not-a-real-profile", "--dry-run");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("Unknown profile", r.StdErr);
            Assert.Contains("live-triage", r.StdErr);
        }

        [Fact]
        public void Profile_and_sources_are_mutually_exclusive()
        {
            var r = CliRunner.Run("collect", "--profile", "live-triage", "--sources", "process-list", "--dry-run");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("mutually exclusive", r.StdErr);
        }

        [Fact]
        public void Naked_local_from_is_rejected()
        {
            var r = CliRunner.Run("collect", "--sources", "process-list",
                "--from", "2026-05-01T00:00:00", "--no-time-filter", "process-list");
            Assert.Equal(2, r.ExitCode);
        }

        [Fact]
        public void From_after_to_is_rejected()
        {
            var r = CliRunner.Run("collect", "--sources", "process-list",
                "--from", "2026-05-10T00:00:00Z", "--to", "2026-05-01T00:00:00Z",
                "--no-time-filter", "process-list");
            Assert.Equal(2, r.ExitCode);
        }

        [Fact]
        public void Time_filter_on_non_time_aware_source_without_no_time_filter_is_rejected()
        {
            var r = CliRunner.Run("collect", "--sources", "process-list",
                "--from", "2026-05-01T00:00:00Z");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("does not support time-range scoping", r.StdErr);
        }

        [Fact]
        public void Negative_pid_is_rejected()
        {
            var r = CliRunner.Run("collect", "--sources", "process-list", "--pid", "-3");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("--pid", r.StdErr);
        }

        [Fact]
        public void Memory_ceiling_below_64_mb_is_rejected()
        {
            var r = CliRunner.Run("collect", "--sources", "process-list", "--memory-ceiling-mb", "16");
            Assert.Equal(2, r.ExitCode);
        }
    }
}
