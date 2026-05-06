using System;
using System.Collections.Generic;
using System.IO;
using Forens.Core.Collection;
using Forens.Core.Collectors.Lnk;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Parses each .lnk file under the operator's profile and emits the
    /// fully-decoded ShellLink content: target absolute path (LocalBasePath),
    /// volume drive type/serial/label, target file's creation/access/write
    /// FILETIME timestamps, working directory, command-line arguments, icon
    /// location. Complements <see cref="LnkFilesSource"/> which only enumerates
    /// the file inventory + filesystem metadata.
    /// </summary>
    public sealed class LnkTargetsSource : IArtifactSource
    {
        public const string SourceId = "lnk-targets";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "LNK Targets (parsed)",
            description: "Microsoft Shell-Link binary parser: target absolute path, volume metadata, target FILETIME timestamps, working dir, arguments.",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("lnk-targets.jsonl"))
            {
                int parseErrors = 0;
                int recordsEmitted = 0;
                foreach (var path in EnumerateLnkFiles())
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    var record = BuildRecord(path);
                    if (record == null) continue;

                    if (!string.IsNullOrEmpty(record.LocalBasePath) &&
                        !ctx.ProcessFilter.IncludesImagePath(record.LocalBasePath))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(record.ParseError)) parseErrors++;
                    jl.Write(record);
                    writer.RecordItem();
                    recordsEmitted++;
                }
                if (parseErrors > 0)
                {
                    writer.RecordPartial(parseErrors + " of " + recordsEmitted + " LNK file(s) had a parse error");
                }
            }
        }

        private static IEnumerable<string> EnumerateLnkFiles()
        {
            // Reuse LnkFilesSource's known roots conceptually — but list .lnk only here
            // (skip .automaticDestinations-ms / .customDestinations-ms which use CFB
            // compound-file format requiring separate parsing).
            string[] dirs =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.SendTo),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in files) yield return f;
            }
        }

        internal static LnkRecord BuildRecord(string path)
        {
            FileInfo info;
            try { info = new FileInfo(path); }
            catch { return null; }

            ParsedLnk parsed;
            try { parsed = LnkParser.ParseFile(path); }
            catch (Exception ex) { parsed = new ParsedLnk { ParseError = "Unhandled parser exception: " + ex.Message }; }

            return new LnkRecord
            {
                LnkPath = info.FullName,
                LnkSizeBytes = info.Length,
                LnkLastModifiedUtc = info.LastWriteTimeUtc,
                HeaderValid = parsed.HeaderValid,
                LinkFlagsHex = "0x" + parsed.LinkFlags.ToString("X8"),
                FileAttributesHex = "0x" + parsed.FileAttributes.ToString("X8"),
                LocalBasePath = parsed.LocalBasePath,
                CommonPathSuffix = parsed.CommonPathSuffix,
                DriveType = parsed.DriveType,
                DriveTypeName = parsed.DriveTypeName,
                DriveSerialHex = parsed.DriveSerialNumber.HasValue
                    ? "0x" + parsed.DriveSerialNumber.Value.ToString("X8")
                    : null,
                VolumeLabel = parsed.VolumeLabel,
                TargetFileSize = parsed.TargetFileSize,
                TargetCreationUtc = parsed.TargetCreationUtc,
                TargetAccessUtc = parsed.TargetAccessUtc,
                TargetWriteUtc = parsed.TargetWriteUtc,
                Name = parsed.Name,
                RelativePath = parsed.RelativePath,
                WorkingDir = parsed.WorkingDir,
                Arguments = parsed.Arguments,
                IconLocation = parsed.IconLocation,
                IconIndex = parsed.IconIndex,
                ShowCommand = parsed.ShowCommand,
                ParseError = parsed.ParseError
            };
        }

        internal sealed class LnkRecord
        {
            public string LnkPath { get; set; }
            public long LnkSizeBytes { get; set; }
            public DateTime LnkLastModifiedUtc { get; set; }
            public bool HeaderValid { get; set; }
            public string LinkFlagsHex { get; set; }
            public string FileAttributesHex { get; set; }
            public string LocalBasePath { get; set; }
            public string CommonPathSuffix { get; set; }
            public uint? DriveType { get; set; }
            public string DriveTypeName { get; set; }
            public string DriveSerialHex { get; set; }
            public string VolumeLabel { get; set; }
            public uint TargetFileSize { get; set; }
            public DateTimeOffset? TargetCreationUtc { get; set; }
            public DateTimeOffset? TargetAccessUtc { get; set; }
            public DateTimeOffset? TargetWriteUtc { get; set; }
            public string Name { get; set; }
            public string RelativePath { get; set; }
            public string WorkingDir { get; set; }
            public string Arguments { get; set; }
            public string IconLocation { get; set; }
            public int IconIndex { get; set; }
            public int ShowCommand { get; set; }
            public string ParseError { get; set; }
        }
    }
}
