using System;
using System.Collections.Generic;
using System.Text;
using Forens.Common.Host;
using Forens.Core.Collection;
using McMaster.Extensions.CommandLineUtils;

namespace Forens.Cli.Commands
{
    public sealed class PreRunSummary
    {
        public sealed class SourceRow
        {
            public string Id;
            public string DisplayName;
            public bool RequiresElevation;
            public PreconditionResult Precondition;
            public string PreconditionReason;
        }

        public static void Print(
            IConsole console,
            string profileName,
            Guid runId,
            string outputDirName,
            ElevationState elevation,
            DateTimeOffset? timeFrom,
            DateTimeOffset? timeTo,
            IReadOnlyList<int> pids,
            IReadOnlyList<string> processNames,
            IReadOnlyList<SourceRow> rows)
        {
            console.WriteLine("forens collect — pre-run summary");
            if (!string.IsNullOrEmpty(profileName))
                console.WriteLine("   Profile: " + profileName + "  (" + rows.Count + " sources)");
            else
                console.WriteLine("   Sources: " + rows.Count);
            console.WriteLine("   Run id: " + runId.ToString("D"));
            console.WriteLine("   Output dir: " + outputDirName);

            var filterParts = new List<string>();
            if (timeFrom.HasValue) filterParts.Add("from=" + timeFrom.Value.UtcDateTime.ToString("o"));
            if (timeTo.HasValue) filterParts.Add("to=" + timeTo.Value.UtcDateTime.ToString("o"));
            if (pids != null && pids.Count > 0) filterParts.Add("pids=[" + string.Join(",", pids) + "]");
            if (processNames != null && processNames.Count > 0) filterParts.Add("processes=[" + string.Join(",", processNames) + "]");
            console.WriteLine("   Filters: " + (filterParts.Count == 0 ? "(none)" : string.Join(", ", filterParts)));
            console.WriteLine("   Elevation: " + elevation);

            var skips = new List<SourceRow>();
            var runs = new List<SourceRow>();
            foreach (var r in rows)
            {
                if (r.Precondition == PreconditionResult.Ok) runs.Add(r);
                else skips.Add(r);
            }

            if (skips.Count > 0)
            {
                console.WriteLine("   Sources to skip at this privilege level (" + skips.Count + "):");
                foreach (var r in skips)
                {
                    console.WriteLine("     " + r.Id.PadRight(28) + " — " + (r.PreconditionReason ?? "skipped"));
                }
            }
            console.WriteLine("   Sources to run (" + runs.Count + "):");
            foreach (var r in runs)
            {
                console.WriteLine("     " + r.Id.PadRight(28) + "   " + (r.DisplayName ?? ""));
            }
        }
    }
}
