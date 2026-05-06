using System;
using System.Collections.Generic;
using Forens.Core.Collection;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class EnvironmentSource : IArtifactSource
    {
        public const string SourceId = "environment";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Environment Variables",
            description: "Persistent environment variables from HKLM and HKCU registry plus the live process environment.",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveSystem, ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 4,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("environment.jsonl"))
            {
                EmitFromKey("Machine", RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", ctx, writer, jl);
                EmitFromKey("User", RegistryHive.CurrentUser, "Environment", ctx, writer, jl);
                EmitFromKey("Volatile", RegistryHive.CurrentUser, "Volatile Environment", ctx, writer, jl);
                EmitProcessEnv(ctx, writer, jl);
            }
        }

        private static void EmitFromKey(string scope, RegistryHive hive, string subKey, CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            try
            {
                foreach (var v in RegistryReader.EnumerateValues(hive, RegistryView.Registry64, subKey))
                {
                    jl.Write(new EnvRecord
                    {
                        Scope = scope,
                        Source = "Registry",
                        Hive = v.Hive,
                        KeyPath = v.KeyPath,
                        Name = v.ValueName,
                        Value = v.Value?.ToString(),
                        Kind = v.Kind.ToString()
                    });
                    writer.RecordItem();
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "Failed reading environment scope {Scope}", scope);
                writer.RecordPartial("Failed reading environment scope " + scope);
            }
        }

        private static void EmitProcessEnv(CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            try
            {
                var entries = Environment.GetEnvironmentVariables();
                foreach (System.Collections.DictionaryEntry kv in entries)
                {
                    jl.Write(new EnvRecord
                    {
                        Scope = "Process",
                        Source = "Environment.GetEnvironmentVariables",
                        Name = kv.Key?.ToString(),
                        Value = kv.Value?.ToString(),
                        Kind = "String"
                    });
                    writer.RecordItem();
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "Process env enumeration failed");
                writer.RecordPartial("Process env enumeration failed");
            }
        }

        private sealed class EnvRecord
        {
            public string Scope { get; set; }
            public string Source { get; set; }
            public string Hive { get; set; }
            public string KeyPath { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
            public string Kind { get; set; }
        }
    }
}
