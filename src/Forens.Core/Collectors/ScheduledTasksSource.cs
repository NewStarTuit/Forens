using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class ScheduledTasksSource : IArtifactSource
    {
        public const string SourceId = "scheduled-tasks";
        private const string TaskScheduleNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Scheduled Tasks",
            description: "Windows Task Scheduler tasks (collected via schtasks.exe /query /xml).",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            string exe = SchtasksPath();
            if (!File.Exists(exe))
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "schtasks.exe not found: " + exe);
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string xml;
            try { xml = RunSchtasks(); }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "schtasks.exe invocation failed");
                writer.RecordPartial("schtasks.exe failed: " + ex.Message);
                return;
            }
            if (string.IsNullOrWhiteSpace(xml))
            {
                writer.RecordPartial("schtasks.exe returned empty output");
                return;
            }

            var records = ParseTasksXml(xml, ctx).ToList();
            using (var jl = writer.OpenJsonlFile("scheduled-tasks.jsonl"))
            {
                foreach (var r in records)
                {
                    if (!string.IsNullOrEmpty(r.ActionImagePath) &&
                        !ctx.ProcessFilter.IncludesImagePath(r.ActionImagePath))
                    {
                        continue;
                    }
                    jl.Write(r);
                    writer.RecordItem();
                }
            }
        }

        private static string SchtasksPath()
        {
            string sysroot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return Path.Combine(sysroot, "System32", "schtasks.exe");
        }

        private static string RunSchtasks()
        {
            // schtasks outputs UTF-16 LE bytes; read as bytes and decode explicitly.
            var psi = new ProcessStartInfo(SchtasksPath(), "/query /xml ONE")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                byte[] outBytes;
                using (var ms = new MemoryStream())
                {
                    p.StandardOutput.BaseStream.CopyTo(ms);
                    outBytes = ms.ToArray();
                }
                p.StandardError.ReadToEnd();
                p.WaitForExit(60_000);
                return DecodeOutput(outBytes);
            }
        }

        internal static string DecodeOutput(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            // UTF-16 LE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            // UTF-16 BE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            // UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            // Heuristic: if every other byte is 0, treat as UTF-16 LE without BOM
            if (bytes.Length >= 4 && bytes[1] == 0 && bytes[3] == 0)
                return Encoding.Unicode.GetString(bytes);
            // Fallback: console default (often OEM on Windows)
            return Encoding.Default.GetString(bytes);
        }

        internal static IEnumerable<TaskRecord> ParseTasksXml(string xml, CollectionContext ctx)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (Exception ex)
            {
                ctx?.Logger?.Warning(ex, "Failed to parse schtasks XML output");
                yield break;
            }
            if (doc.Root == null) yield break;

            // schtasks /xml ONE wraps every task in a <Tasks> root with each <Task> having
            // its own default xmlns. We iterate descendants by local name to handle either
            // the wrapper-or-no-wrapper case.
            foreach (var taskEl in doc.Descendants().Where(e => e.Name.LocalName == "Task"))
            {
                var record = ParseOne(taskEl);
                if (record != null) yield return record;
            }
        }

        private static TaskRecord ParseOne(XElement taskEl)
        {
            XNamespace ns = taskEl.GetDefaultNamespace();
            if (ns == XNamespace.None) ns = TaskScheduleNs;

            var registration = taskEl.Element(ns + "RegistrationInfo");
            var settings = taskEl.Element(ns + "Settings");
            var principals = taskEl.Element(ns + "Principals");
            var actions = taskEl.Element(ns + "Actions");
            var triggers = taskEl.Element(ns + "Triggers");

            string author = registration?.Element(ns + "Author")?.Value;
            string description = registration?.Element(ns + "Description")?.Value;
            string registrationDate = registration?.Element(ns + "Date")?.Value;
            string uri = registration?.Element(ns + "URI")?.Value;

            bool? enabled = ParseNullableBool(settings?.Element(ns + "Enabled")?.Value) ?? true;
            string hidden = settings?.Element(ns + "Hidden")?.Value;

            var firstPrincipal = principals?.Elements(ns + "Principal").FirstOrDefault();
            string principalUserId = firstPrincipal?.Element(ns + "UserId")?.Value;
            string principalRunLevel = firstPrincipal?.Element(ns + "RunLevel")?.Value;
            string principalLogonType = firstPrincipal?.Element(ns + "LogonType")?.Value;

            var firstExec = actions?.Element(ns + "Exec");
            string actionCommand = firstExec?.Element(ns + "Command")?.Value;
            string actionArgs = firstExec?.Element(ns + "Arguments")?.Value;
            string actionWorkingDir = firstExec?.Element(ns + "WorkingDirectory")?.Value;
            string actionImagePath = string.IsNullOrEmpty(actionCommand)
                ? null
                : ExpandEnv(actionCommand);

            var triggerSummaries = new List<string>();
            if (triggers != null)
            {
                foreach (var t in triggers.Elements())
                    triggerSummaries.Add(t.Name.LocalName);
            }

            return new TaskRecord
            {
                Uri = uri,
                Author = author,
                Description = description,
                RegistrationDate = registrationDate,
                Enabled = enabled,
                Hidden = ParseNullableBool(hidden),
                PrincipalUserId = principalUserId,
                PrincipalRunLevel = principalRunLevel,
                PrincipalLogonType = principalLogonType,
                ActionCommand = actionCommand,
                ActionArguments = actionArgs,
                ActionWorkingDirectory = actionWorkingDir,
                ActionImagePath = actionImagePath,
                Triggers = triggerSummaries
            };
        }

        private static string ExpandEnv(string s)
        {
            try { return Environment.ExpandEnvironmentVariables(s); }
            catch { return s; }
        }

        private static bool? ParseNullableBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (bool.TryParse(s, out var b)) return b;
            return null;
        }

        internal sealed class TaskRecord
        {
            public string Uri { get; set; }
            public string Author { get; set; }
            public string Description { get; set; }
            public string RegistrationDate { get; set; }
            public bool? Enabled { get; set; }
            public bool? Hidden { get; set; }
            public string PrincipalUserId { get; set; }
            public string PrincipalRunLevel { get; set; }
            public string PrincipalLogonType { get; set; }
            public string ActionCommand { get; set; }
            public string ActionArguments { get; set; }
            public string ActionWorkingDirectory { get; set; }
            public string ActionImagePath { get; set; }
            public List<string> Triggers { get; set; }
        }
    }
}
