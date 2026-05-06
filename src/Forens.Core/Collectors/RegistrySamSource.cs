using System;
using System.IO;
using Forens.Core.Collection;
using Forens.Core.Collectors.Hive;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Local-account metadata from the SAM hive (RegLoadAppKey on
    /// %SystemRoot%\System32\config\SAM). Requires elevation +
    /// SeBackupPrivilege. Emits one record per local user with username,
    /// RID, last-logon time, password-last-set time, account flags, and
    /// logon count.
    ///
    /// **Spec compliance**: This source NEVER extracts NT or LM password
    /// hashes (per feature 002 spec FR-013 / SC-006). Only metadata fields
    /// from the F-record are read.
    /// </summary>
    public sealed class RegistrySamSource : IArtifactSource
    {
        public const string SourceId = "registry-sam";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Registry SAM (local accounts)",
            description: "Local user accounts parsed from the SAM hive — RID, last logon, password-last-set, account flags, logon count. Never extracts password hashes.",
            category: Category.User,
            requiresElevation: true,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveSam },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            if (ctx.Elevation != Forens.Common.Host.ElevationState.Elevated)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Reading the SAM hive requires administrator + SeBackupPrivilege");
            }
            string path = HivePath();
            if (!File.Exists(path))
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "SAM hive file not found: " + path);
            }
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string path = HivePath();
            using (var jl = writer.OpenJsonlFile("registry-sam.jsonl"))
            {
                RegistryKey root;
                try { root = HiveLoader.Open(path); }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "Failed to mount SAM hive");
                    writer.RecordPartial("Failed to mount SAM hive: " + ex.Message);
                    return;
                }

                using (root)
                using (var users = root.OpenSubKey(@"SAM\Domains\Account\Users", writable: false))
                {
                    if (users == null)
                    {
                        writer.RecordPartial("SAM\\Domains\\Account\\Users key not present in mounted hive");
                        return;
                    }
                    string[] subkeys;
                    try { subkeys = users.GetSubKeyNames(); }
                    catch (Exception ex)
                    {
                        writer.RecordPartial("Cannot enumerate Users subkeys: " + ex.Message);
                        return;
                    }

                    foreach (var name in subkeys)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        if (string.Equals(name, "Names", StringComparison.OrdinalIgnoreCase)) continue;
                        EmitUser(users, name, ctx, writer, jl);
                    }
                }
            }
        }

        private static void EmitUser(RegistryKey usersKey, string ridHex,
            CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            uint rid;
            if (!uint.TryParse(ridHex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out rid))
            {
                return;
            }
            using (var ridKey = usersKey.OpenSubKey(ridHex, writable: false))
            {
                if (ridKey == null) return;
                byte[] f = ridKey.GetValue("F") as byte[];
                byte[] v = ridKey.GetValue("V") as byte[];

                var record = ParseFRecord(f);
                record.RidHex = ridHex;
                record.Rid = rid;
                record.UserName = ExtractUsernameFromV(v);
                jl.Write(record);
                writer.RecordItem();
            }
        }

        // F record offsets (binary, 80 bytes typical):
        //   0x08: Last logon time (FILETIME, 8 bytes)
        //   0x18: Password last set (FILETIME)
        //   0x20: Account expires (FILETIME)
        //   0x28: Last password fail (FILETIME)
        //   0x30: User RID (uint32)
        //   0x34: Group RID (uint32)
        //   0x38: Account flags (uint32)
        //   0x40: Failed login count (uint16)
        //   0x42: Logon count (uint16)
        internal static SamUserRecord ParseFRecord(byte[] f)
        {
            var r = new SamUserRecord();
            if (f == null || f.Length < 0x44) return r;
            r.LastLogonUtc = SafeFileTime(BitConverter.ToInt64(f, 0x08));
            r.PasswordLastSetUtc = SafeFileTime(BitConverter.ToInt64(f, 0x18));
            r.AccountExpiresUtc = SafeFileTime(BitConverter.ToInt64(f, 0x20));
            r.LastFailedLoginUtc = SafeFileTime(BitConverter.ToInt64(f, 0x28));
            uint flags = BitConverter.ToUInt32(f, 0x38);
            r.AccountFlagsHex = "0x" + flags.ToString("X8");
            r.AccountFlagsDecoded = DecodeFlags(flags);
            r.FailedLoginCount = BitConverter.ToUInt16(f, 0x40);
            r.LogonCount = BitConverter.ToUInt16(f, 0x42);
            return r;
        }

        // V record begins with a header table of (offset, length, ...) triples for each
        // string field. The header is 0xCC bytes; the username sits at offsets in that
        // table. The simplest extraction is: search the buffer for embedded UTF-16
        // string runs and pick the first one that looks like an account name.
        internal static string ExtractUsernameFromV(byte[] v)
        {
            if (v == null || v.Length < 0xCC) return null;
            // Header: at offset 0x0C..0x10 is the Name's relative offset (uint32)
            //         at offset 0x10..0x14 is Name's length in bytes (uint32)
            // Names are at relativeOffset + 0xCC.
            uint nameRel = BitConverter.ToUInt32(v, 0x0C);
            uint nameLen = BitConverter.ToUInt32(v, 0x10);
            int absOffset = (int)nameRel + 0xCC;
            if (nameLen == 0 || absOffset < 0xCC || absOffset + nameLen > v.Length || nameLen > 1024)
                return null;
            try
            {
                return System.Text.Encoding.Unicode.GetString(v, absOffset, (int)nameLen);
            }
            catch { return null; }
        }

        private static System.Collections.Generic.List<string> DecodeFlags(uint flags)
        {
            var list = new System.Collections.Generic.List<string>();
            if ((flags & 0x0001) != 0) list.Add("Disabled");
            if ((flags & 0x0002) != 0) list.Add("HomeDirRequired");
            if ((flags & 0x0004) != 0) list.Add("PasswordNotRequired");
            if ((flags & 0x0008) != 0) list.Add("TempDuplicateAccount");
            if ((flags & 0x0010) != 0) list.Add("NormalAccount");
            if ((flags & 0x0020) != 0) list.Add("MnsLogonAccount");
            if ((flags & 0x0040) != 0) list.Add("InterDomainTrustAccount");
            if ((flags & 0x0080) != 0) list.Add("WorkstationTrustAccount");
            if ((flags & 0x0100) != 0) list.Add("ServerTrustAccount");
            if ((flags & 0x0200) != 0) list.Add("DontExpirePassword");
            if ((flags & 0x0400) != 0) list.Add("AutoLocked");
            return list;
        }

        private static DateTimeOffset? SafeFileTime(long ft)
        {
            if (ft <= 0) return null;
            try { return DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
            catch { return null; }
        }

        private static string HivePath()
        {
            string sysroot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return Path.Combine(sysroot, "System32", "config", "SAM");
        }

        internal sealed class SamUserRecord
        {
            public string RidHex { get; set; }
            public uint Rid { get; set; }
            public string UserName { get; set; }
            public DateTimeOffset? LastLogonUtc { get; set; }
            public DateTimeOffset? PasswordLastSetUtc { get; set; }
            public DateTimeOffset? AccountExpiresUtc { get; set; }
            public DateTimeOffset? LastFailedLoginUtc { get; set; }
            public string AccountFlagsHex { get; set; }
            public System.Collections.Generic.List<string> AccountFlagsDecoded { get; set; }
            public ushort? FailedLoginCount { get; set; }
            public ushort? LogonCount { get; set; }
        }
    }
}
