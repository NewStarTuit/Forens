using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class ProcessListSource : IArtifactSource
    {
        public const string SourceId = "process-list";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Running Processes",
            description: "Snapshot of running processes (PID, image path, parent PID, command line).",
            category: Category.Process,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.LivePid,
            contendedResources: new[] { ContendedResource.WmiCimV2 },
            estimatedMemoryMB: 32,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            var wmi = QueryWmiInfo(ctx);
            using (var jl = writer.OpenJsonlFile("processes.jsonl"))
            {
                Process[] processes;
                try { processes = Process.GetProcesses(); }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "Process.GetProcesses failed");
                    writer.RecordPartial("Process.GetProcesses failed: " + ex.Message);
                    return;
                }

                try
                {
                    foreach (var p in processes)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            int pid;
                            try { pid = p.Id; } catch { continue; }
                            if (!ctx.ProcessFilter.Includes(pid)) continue;

                            WmiInfo info;
                            wmi.TryGetValue(pid, out info);

                            var record = new ProcessRecord
                            {
                                Pid = pid,
                                ProcessName = SafeProcessName(p),
                                ImagePath = info != null ? info.ExecutablePath : null,
                                CommandLine = info != null ? info.CommandLine : null,
                                ParentPid = info != null ? info.ParentPid : (int?)null,
                                StartTimeUtc = SafeStartTime(p),
                                SessionId = SafeSessionId(p),
                                ThreadCount = SafeThreadCount(p),
                                WorkingSet64 = SafeWorkingSet(p)
                            };
                            jl.Write(record);
                            writer.RecordItem();
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            ctx.Logger.Verbose(ex, "Skipped a process due to error");
                            writer.RecordPartial("One or more processes could not be enumerated");
                        }
                    }
                }
                finally
                {
                    foreach (var p in processes) { try { p.Dispose(); } catch { } }
                }
            }
        }

        private static Dictionary<int, WmiInfo> QueryWmiInfo(CollectionContext ctx)
        {
            var dict = new Dictionary<int, WmiInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, ExecutablePath, CommandLine FROM Win32_Process"))
                using (var collection = searcher.Get())
                {
                    foreach (var mo in collection)
                    {
                        try
                        {
                            int pid = ToInt(mo["ProcessId"]);
                            if (pid <= 0) continue;
                            dict[pid] = new WmiInfo
                            {
                                ParentPid = ToInt(mo["ParentProcessId"]),
                                ExecutablePath = mo["ExecutablePath"] as string,
                                CommandLine = mo["CommandLine"] as string
                            };
                        }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "WMI Win32_Process query failed; command lines will be unavailable");
            }
            return dict;
        }

        private static int ToInt(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        private static string SafeProcessName(Process p)
        {
            try { return p.ProcessName; } catch { return null; }
        }

        private static DateTimeOffset? SafeStartTime(Process p)
        {
            try { return new DateTimeOffset(p.StartTime.ToUniversalTime(), TimeSpan.Zero); }
            catch { return null; }
        }

        private static int? SafeSessionId(Process p)
        {
            try { return p.SessionId; } catch { return null; }
        }

        private static int? SafeThreadCount(Process p)
        {
            try { return p.Threads != null ? p.Threads.Count : (int?)null; } catch { return null; }
        }

        private static long? SafeWorkingSet(Process p)
        {
            try { return p.WorkingSet64; } catch { return null; }
        }

        private sealed class WmiInfo
        {
            public int ParentPid;
            public string ExecutablePath;
            public string CommandLine;
        }

        private sealed class ProcessRecord
        {
            public int Pid { get; set; }
            public string ProcessName { get; set; }
            public string ImagePath { get; set; }
            public string CommandLine { get; set; }
            public int? ParentPid { get; set; }
            public DateTimeOffset? StartTimeUtc { get; set; }
            public int? SessionId { get; set; }
            public int? ThreadCount { get; set; }
            public long? WorkingSet64 { get; set; }
        }
    }
}
