using System;
using System.Collections.Generic;
using Forens.Core.Collection;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Enumerates Windows Defender exclusion lists from the registry.
    /// High forensic value: attackers commonly configure exclusions to hide
    /// malware from Defender scans. Reads:
    ///   HKLM\Software\Microsoft\Windows Defender\Exclusions\{Paths|Extensions|Processes|IpAddresses}
    ///   HKLM\Software\Policies\Microsoft\Windows Defender\Exclusions\{...}
    /// Both the "real" exclusions (Defender service) and the "policy"
    /// exclusions (GPO/MDM-applied) are walked separately so the analyst
    /// can spot policy-vs-direct discrepancies.
    /// </summary>
    public sealed class DefenderExclusionsSource : IArtifactSource
    {
        public const string SourceId = "defender-exclusions";

        private static readonly string[] ExclusionRoots =
        {
            @"Software\Microsoft\Windows Defender\Exclusions",
            @"Software\Policies\Microsoft\Windows Defender\Exclusions"
        };

        private static readonly string[] CategoryNames =
        {
            "Paths", "Extensions", "Processes", "IpAddresses", "TemporaryPaths"
        };

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Defender Exclusions",
            description: "Defender path/extension/process/IP exclusion lists from HKLM (direct + policy roots).",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: new[] { ContendedResource.RegistryHiveSoftware },
            estimatedMemoryMB: 4,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("defender-exclusions.jsonl"))
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                int totalEmitted = 0;
                int accessDeniedRoots = 0;
                foreach (var rootPath in ExclusionRoots)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    string scope = rootPath.Contains("Policies") ? "Policy" : "Direct";
                    RegistryKey rootKey = null;
                    try { rootKey = baseKey.OpenSubKey(rootPath, writable: false); }
                    catch (System.Security.SecurityException)
                    {
                        accessDeniedRoots++;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Failed to open Defender root {Path}", rootPath);
                        continue;
                    }

                    if (rootKey == null) continue;

                    using (rootKey)
                    {
                        foreach (var category in CategoryNames)
                        {
                            RegistryKey catKey = null;
                            try { catKey = rootKey.OpenSubKey(category, writable: false); }
                            catch (System.Security.SecurityException)
                            {
                                continue;
                            }
                            catch (Exception) { continue; }

                            if (catKey == null) continue;

                            using (catKey)
                            {
                                string[] valueNames;
                                try { valueNames = catKey.GetValueNames(); }
                                catch { continue; }

                                foreach (var valueName in valueNames)
                                {
                                    ctx.CancellationToken.ThrowIfCancellationRequested();
                                    object data;
                                    try { data = catKey.GetValue(valueName); }
                                    catch { continue; }

                                    if (category == "Processes" || category == "Paths")
                                    {
                                        if (!string.IsNullOrEmpty(valueName) &&
                                            !ctx.ProcessFilter.IncludesImagePath(valueName))
                                        {
                                            continue;
                                        }
                                    }

                                    jl.Write(new ExclusionRecord
                                    {
                                        Scope = scope,
                                        RegistryRoot = rootPath,
                                        Category = category,
                                        ExclusionValue = valueName,
                                        DataValue = data?.ToString()
                                    });
                                    writer.RecordItem();
                                    totalEmitted++;
                                }
                            }
                        }
                    }
                }
                if (accessDeniedRoots > 0)
                {
                    writer.RecordPartial(string.Format(
                        "{0} of {1} Defender root key(s) were access-denied (typical on Win 10/11 without admin); add elevation to enumerate the full direct exclusion list.",
                        accessDeniedRoots, ExclusionRoots.Length));
                }
                if (totalEmitted == 0)
                {
                    ctx.Logger.Verbose("No Defender exclusions found (or Defender not installed / all roots access-denied)");
                }
            }
        }

        private sealed class ExclusionRecord
        {
            public string Scope { get; set; }
            public string RegistryRoot { get; set; }
            public string Category { get; set; }
            public string ExclusionValue { get; set; }
            public string DataValue { get; set; }
        }
    }
}
