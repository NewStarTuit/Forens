using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class DefenderDetectionHistorySource : IArtifactSource
    {
        public const string SourceId = "defender-detection-history";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Defender Detection History (MPLog files)",
            description: "Windows Defender file-based logs (%ProgramData%\\Microsoft\\Windows Defender\\Support\\MPLog-*.log).",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 32,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            string dir = SupportDir();
            if (!Directory.Exists(dir))
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Defender Support directory not present: " + dir);
            try
            {
                Directory.GetFiles(dir, "MPLog-*.log");
                return SourcePrecondition.Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Cannot read Defender Support directory");
            }
            catch (Exception ex)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Cannot list Defender MPLog files: " + ex.Message);
            }
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string dir = SupportDir();
            string[] files;
            try { files = Directory.GetFiles(dir, "MPLog-*.log"); }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Failed to list Defender Support directory");
                writer.RecordPartial("Failed to list Defender Support directory: " + ex.Message);
                return;
            }

            using (var inv = writer.OpenJsonlFile("mplog-inventory.jsonl"))
            using (var detections = writer.OpenJsonlFile("detections.jsonl"))
            {
                foreach (var file in files)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(file);
                    inv.Write(new MpLogFileRecord
                    {
                        FileName = info.Name,
                        FullPath = info.FullName,
                        SizeBytes = info.Length,
                        CreatedUtc = info.CreationTimeUtc,
                        LastModifiedUtc = info.LastWriteTimeUtc
                    });
                    writer.RecordItem();

                    try
                    {
                        ParseDetections(file, detections, writer, ctx);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        writer.RecordPartial("Some MPLog files were not readable");
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Failed to parse MPLog {File}", file);
                        writer.RecordPartial("One or more MPLog files failed to parse");
                    }
                }
            }
        }

        // MPLog files are plain text with timestamped lines. Detection-of-interest lines
        // typically contain markers like:
        //   "Threat ", "ThreatName=", "DETECTION", "DETECTIONEVENT", "RTPSig",
        //   "DetectFile", "EngineUpdated", "Signatures", "RemediateThreat".
        // We extract any line containing "Threat " or "DETECTIONEVENT" or "DetectFile".
        private static readonly string[] DetectionMarkers =
        {
            "DETECTIONEVENT",
            "DetectFile",
            "RTPSig",
            "Threat ",
            "ThreatName",
            "RemediateThreat"
        };

        private static void ParseDetections(string filePath, IRecordWriter detections, ISourceWriter writer, CollectionContext ctx)
        {
            // Files can be hundreds of MB. Stream line-by-line.
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, System.Text.Encoding.Default, true, 64 * 1024))
            {
                string line;
                long lineNumber = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNumber++;
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(line)) continue;
                    foreach (var marker in DetectionMarkers)
                    {
                        if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            detections.Write(new DetectionLineRecord
                            {
                                File = Path.GetFileName(filePath),
                                LineNumber = lineNumber,
                                Marker = marker,
                                Text = line.Length > 4000 ? line.Substring(0, 4000) : line
                            });
                            writer.RecordItem();
                            break;
                        }
                    }
                }
            }
        }

        private static string SupportDir()
        {
            string programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
            return Path.Combine(programData, "Microsoft", "Windows Defender", "Support");
        }

        private sealed class MpLogFileRecord
        {
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public long SizeBytes { get; set; }
            public DateTime CreatedUtc { get; set; }
            public DateTime LastModifiedUtc { get; set; }
        }

        private sealed class DetectionLineRecord
        {
            public string File { get; set; }
            public long LineNumber { get; set; }
            public string Marker { get; set; }
            public string Text { get; set; }
        }
    }
}
