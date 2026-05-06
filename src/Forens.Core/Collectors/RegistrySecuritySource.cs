using System;
using System.Collections.Generic;
using System.IO;
using Forens.Core.Collection;
using Forens.Core.Collectors.Hive;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Reads the SECURITY hive and emits forensic METADATA only:
    ///   - Audit policy (PolAdtEv) decoded into per-category Success/Failure flags.
    ///   - Account domain SID (PolAcDmS).
    ///   - LSA Secret names + last-set timestamps from Policy\Secrets\&lt;name&gt;.
    ///
    /// **Spec compliance**: per FR-013/SC-006 this source NEVER reads or emits
    /// the secret value bytes (CurrVal/OldVal). Only secret names + timestamps.
    /// A reflection test enforces no `*secret*`/`*hash*` value field exists on
    /// the public record types.
    ///
    /// Requires administrator + SeBackupPrivilege to mount the hive file
    /// (RegLoadAppKey on %SystemRoot%\System32\config\SECURITY).
    /// </summary>
    public sealed class RegistrySecuritySource : IArtifactSource
    {
        public const string SourceId = "registry-security";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Registry SECURITY (audit policy + LSA metadata)",
            description: "Audit policy categories, account domain SID, and LSA Secret names (no values) from the SECURITY hive.",
            category: Category.Persistence,
            requiresElevation: true,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveSecurity },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            if (ctx.Elevation != Forens.Common.Host.ElevationState.Elevated)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Reading the SECURITY hive requires administrator + SeBackupPrivilege");
            }
            string path = HivePath();
            if (!File.Exists(path))
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "SECURITY hive file not found: " + path);
            }
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string path = HivePath();
            RegistryKey root;
            try { root = HiveLoader.Open(path); }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Failed to mount SECURITY hive");
                writer.RecordPartial("Failed to mount SECURITY hive: " + ex.Message);
                return;
            }

            using (root)
            {
                EmitAuditPolicy(root, writer);
                EmitDomainSid(root, writer);
                EmitSecretsMetadata(root, ctx, writer);
            }
        }

        private static void EmitAuditPolicy(RegistryKey root, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("audit-policy.jsonl"))
            using (var key = root.OpenSubKey(@"Policy\PolAdtEv", writable: false))
            {
                if (key == null)
                {
                    writer.RecordPartial("Policy\\PolAdtEv key not present");
                    return;
                }
                byte[] data = key.GetValue(null) as byte[]; // default value
                if (data == null || data.Length < 16)
                {
                    writer.RecordPartial("PolAdtEv default value missing or too short");
                    return;
                }
                jl.Write(ParseAuditPolicy(data));
                writer.RecordItem();
            }
        }

        // PolAdtEv binary blob layout (varies per Windows version; the canonical Win 7+
        // layout is: 4-byte version, 4-byte audit-mode flags, then a sequence of 4-byte
        // category flags. Each flag is 2 bits: 0x1 = Success, 0x2 = Failure.
        // Standard 9 categories on modern Windows:
        //   0: System
        //   1: Logon
        //   2: ObjectAccess
        //   3: PrivilegeUse
        //   4: ProcessTracking (DetailedTracking)
        //   5: PolicyChange
        //   6: AccountManagement
        //   7: DirectoryServiceAccess
        //   8: AccountLogon
        internal static AuditPolicyRecord ParseAuditPolicy(byte[] data)
        {
            var record = new AuditPolicyRecord();
            record.RawByteCount = data.Length;
            record.RawHex = ShellBagsSource.HexEncode(data, 0, Math.Min(data.Length, 64));
            record.AuditingEnabled = data.Length >= 8 && BitConverter.ToInt32(data, 4) != 0;

            // Each per-category flag is 4 bytes starting at offset 8.
            string[] categoryNames =
            {
                "System", "Logon", "ObjectAccess", "PrivilegeUse",
                "DetailedTracking", "PolicyChange", "AccountManagement",
                "DirectoryServiceAccess", "AccountLogon"
            };

            record.Categories = new List<AuditCategoryRecord>();
            for (int i = 0; i < categoryNames.Length; i++)
            {
                int offset = 8 + i * 4;
                if (offset + 4 > data.Length) break;
                int flags = BitConverter.ToInt32(data, offset);
                bool success = (flags & 0x1) != 0;
                bool failure = (flags & 0x2) != 0;
                record.Categories.Add(new AuditCategoryRecord
                {
                    Name = categoryNames[i],
                    AuditSuccess = success,
                    AuditFailure = failure
                });
            }
            return record;
        }

        private static void EmitDomainSid(RegistryKey root, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("account-domain.jsonl"))
            using (var key = root.OpenSubKey(@"Policy\PolAcDmS", writable: false))
            {
                if (key == null) return;
                byte[] sidBytes = key.GetValue(null) as byte[];
                if (sidBytes == null || sidBytes.Length < 12) return;
                string sid = TryFormatSid(sidBytes);
                jl.Write(new DomainSidRecord
                {
                    SidByteCount = sidBytes.Length,
                    Sid = sid,
                    SidHex = ShellBagsSource.HexEncode(sidBytes, 0, Math.Min(sidBytes.Length, 64))
                });
                writer.RecordItem();
            }
        }

        // Best-effort SID formatter for binary SID structure:
        //   byte 0: Revision
        //   byte 1: SubAuthorityCount
        //   bytes 2-7: IdentifierAuthority (6 bytes BIG-endian)
        //   bytes 8+: SubAuthorities (4 bytes each, little-endian)
        internal static string TryFormatSid(byte[] sid)
        {
            if (sid == null || sid.Length < 8) return null;
            try
            {
                byte revision = sid[0];
                byte subAuthCount = sid[1];
                if (sid.Length < 8 + subAuthCount * 4) return null;
                long idAuth = 0;
                for (int i = 2; i < 8; i++) idAuth = (idAuth << 8) | sid[i];
                var sb = new System.Text.StringBuilder();
                sb.Append("S-").Append(revision).Append('-').Append(idAuth);
                for (int i = 0; i < subAuthCount; i++)
                {
                    uint subAuth = BitConverter.ToUInt32(sid, 8 + i * 4);
                    sb.Append('-').Append(subAuth);
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        private static void EmitSecretsMetadata(RegistryKey root, CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("lsa-secrets-metadata.jsonl"))
            using (var key = root.OpenSubKey(@"Policy\Secrets", writable: false))
            {
                if (key == null) return;
                string[] secretNames;
                try { secretNames = key.GetSubKeyNames(); }
                catch (Exception ex)
                {
                    writer.RecordPartial("Cannot enumerate Policy\\Secrets: " + ex.Message);
                    return;
                }

                foreach (var secretName in secretNames)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    using (var secretKey = key.OpenSubKey(secretName, writable: false))
                    {
                        if (secretKey == null) continue;
                        var subNames = secretKey.GetSubKeyNames();
                        var record = new LsaSecretMetadata
                        {
                            Name = secretName,
                            HasCurrentValue = ContainsCaseInsensitive(subNames, "CurrVal"),
                            HasOldValue = ContainsCaseInsensitive(subNames, "OldVal"),
                            CurrentSetTimeUtc = ReadTimeKey(secretKey, "CupdTime"),
                            OldSetTimeUtc = ReadTimeKey(secretKey, "OupdTime"),
                            ChildKeys = new List<string>(subNames)
                        };
                        jl.Write(record);
                        writer.RecordItem();
                    }
                }
            }
        }

        private static bool ContainsCaseInsensitive(string[] arr, string name)
        {
            foreach (var s in arr)
                if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static DateTimeOffset? ReadTimeKey(RegistryKey parent, string subKeyName)
        {
            using (var sub = parent.OpenSubKey(subKeyName, writable: false))
            {
                if (sub == null) return null;
                byte[] data = sub.GetValue(null) as byte[];
                if (data == null || data.Length < 8) return null;
                long ft = BitConverter.ToInt64(data, 0);
                if (ft <= 0) return null;
                try { return DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
                catch { return null; }
            }
        }

        private static string HivePath()
        {
            string sysroot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return Path.Combine(sysroot, "System32", "config", "SECURITY");
        }

        // ---- Records (intentionally NO secret-value or hash fields) ----

        internal sealed class AuditPolicyRecord
        {
            public bool AuditingEnabled { get; set; }
            public int RawByteCount { get; set; }
            public string RawHex { get; set; }
            public List<AuditCategoryRecord> Categories { get; set; }
        }

        internal sealed class AuditCategoryRecord
        {
            public string Name { get; set; }
            public bool AuditSuccess { get; set; }
            public bool AuditFailure { get; set; }
        }

        internal sealed class DomainSidRecord
        {
            public int SidByteCount { get; set; }
            public string Sid { get; set; }
            public string SidHex { get; set; }
        }

        internal sealed class LsaSecretMetadata
        {
            public string Name { get; set; }
            public bool HasCurrentValue { get; set; }
            public bool HasOldValue { get; set; }
            public DateTimeOffset? CurrentSetTimeUtc { get; set; }
            public DateTimeOffset? OldSetTimeUtc { get; set; }
            public List<string> ChildKeys { get; set; }
        }
    }
}
