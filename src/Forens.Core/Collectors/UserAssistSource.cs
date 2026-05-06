using System;
using System.Collections.Generic;
using System.Text;
using Forens.Core.Collection;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class UserAssistSource : IArtifactSource
    {
        public const string SourceId = "userassist";
        private const string UserAssistRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "UserAssist",
            description: "GUI-launched programs per user (decoded from ROT13-encoded NTUSER.DAT registry values).",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: new[] { ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("userassist.jsonl"))
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var root = baseKey.OpenSubKey(UserAssistRoot, writable: false))
                {
                    if (root == null)
                    {
                        ctx.Logger.Verbose("UserAssist root key not present");
                        return;
                    }

                    foreach (var guidName in root.GetSubKeyNames())
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        EmitGuidGroup(root, guidName, ctx, writer, jl);
                    }
                }
            }
        }

        private static void EmitGuidGroup(RegistryKey root, string guidName, CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            try
            {
                using (var guidKey = root.OpenSubKey(guidName, writable: false))
                using (var countKey = guidKey?.OpenSubKey("Count", writable: false))
                {
                    if (countKey == null) return;
                    foreach (var encodedName in countKey.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(encodedName)) continue;
                        string decodedName = Rot13(encodedName);
                        byte[] data;
                        try { data = countKey.GetValue(encodedName) as byte[]; }
                        catch { continue; }
                        if (data == null) continue;

                        var parsed = ParseValue(data);
                        var record = new UserAssistRecord
                        {
                            GuidGroup = guidName,
                            EncodedName = encodedName,
                            DecodedName = decodedName,
                            ImagePath = ExtractImagePath(decodedName),
                            ValueByteCount = data.Length,
                            Format = parsed.Format,
                            RunCount = parsed.RunCount,
                            FocusCountSecondsUsed = parsed.FocusSeconds,
                            LastExecutedUtc = parsed.LastExecutedUtc
                        };

                        if (!string.IsNullOrEmpty(record.ImagePath) &&
                            !ctx.ProcessFilter.IncludesImagePath(record.ImagePath))
                        {
                            continue;
                        }

                        jl.Write(record);
                        writer.RecordItem();
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "Failed to read UserAssist group {Guid}", guidName);
                writer.RecordPartial("Some UserAssist groups failed to read");
            }
        }

        internal static string Rot13(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= 'a' && c <= 'z') sb.Append((char)('a' + (c - 'a' + 13) % 26));
                else if (c >= 'A' && c <= 'Z') sb.Append((char)('A' + (c - 'A' + 13) % 26));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static string ExtractImagePath(string decoded)
        {
            if (string.IsNullOrEmpty(decoded)) return null;
            // UserAssist names often look like:
            //   {GUID}\path\to\program.exe
            //   path\to\program.exe
            //   C:\Path\to\Program.exe
            // GUID prefix is a known-folder id; strip it.
            if (decoded.Length > 1 && decoded[0] == '{')
            {
                int closing = decoded.IndexOf('}');
                if (closing > 0 && closing < decoded.Length - 1)
                {
                    string rest = decoded.Substring(closing + 1).TrimStart('\\', '/');
                    return rest;
                }
            }
            return decoded;
        }

        internal struct ParsedValue
        {
            public string Format;
            public int? RunCount;
            public long? FocusSeconds;
            public DateTimeOffset? LastExecutedUtc;
        }

        internal static ParsedValue ParseValue(byte[] data)
        {
            // Two known formats:
            //   Win XP/2003: 16 bytes — 8-byte session/state header, then 4-byte run count, then 8-byte FILETIME (last run)
            //   Win 7+:      72 bytes — session(4) + runCount(4) + focusCount(4) + focusTime(4) + reserved(48) + lastExec(8) FILETIME + zero(4)
            var p = new ParsedValue();
            if (data == null) { p.Format = "unknown"; return p; }

            if (data.Length >= 72)
            {
                p.Format = "win7+";
                p.RunCount = BitConverter.ToInt32(data, 4);
                p.FocusSeconds = BitConverter.ToInt32(data, 12);
                long ft = BitConverter.ToInt64(data, 60);
                p.LastExecutedUtc = FromFileTimeSafe(ft);
            }
            else if (data.Length >= 16)
            {
                p.Format = "winxp";
                p.RunCount = BitConverter.ToInt32(data, 4);
                long ft = BitConverter.ToInt64(data, 8);
                p.LastExecutedUtc = FromFileTimeSafe(ft);
            }
            else
            {
                p.Format = "short(" + data.Length + ")";
            }
            return p;
        }

        private static DateTimeOffset? FromFileTimeSafe(long fileTime)
        {
            if (fileTime <= 0) return null;
            try { return DateTimeOffset.FromFileTime(fileTime).ToUniversalTime(); }
            catch { return null; }
        }

        private sealed class UserAssistRecord
        {
            public string GuidGroup { get; set; }
            public string EncodedName { get; set; }
            public string DecodedName { get; set; }
            public string ImagePath { get; set; }
            public int ValueByteCount { get; set; }
            public string Format { get; set; }
            public int? RunCount { get; set; }
            public long? FocusCountSecondsUsed { get; set; }
            public DateTimeOffset? LastExecutedUtc { get; set; }
        }
    }
}
