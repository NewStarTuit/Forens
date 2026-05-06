using System;
using System.ComponentModel;
using Forens.Core.Collection;
using Forens.Core.Collectors.Ntfs;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Queries the USN change journal of the system volume and emits two output
    /// files: the journal metadata header (`usn-journal-header.jsonl` — a single
    /// record with journal id, first/next USN, max size) and the per-record
    /// stream from FSCTL_ENUM_USN_DATA over the [LowestValidUsn..NextUsn] range
    /// (`usn-journal.jsonl`). Each record carries FRN, ParentFRN, USN, TimeStamp,
    /// decoded Reason flags, decoded FileAttributes flags, and FileName.
    ///
    /// Requires elevation + SeBackupPrivilege (CreateFile on \\.\&lt;volume&gt; is
    /// access-denied for non-admin). Capped at 5,000,000 records by default to
    /// keep output bounded; raise via re-collection if needed.
    /// </summary>
    public sealed class UsnJournalSource : IArtifactSource
    {
        public const string SourceId = "usn-journal";
        private const int MaxRecords = 5_000_000;

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "USN Change Journal",
            description: "NTFS USN journal metadata + per-file change records (FSCTL_ENUM_USN_DATA on \\\\.\\<volume>).",
            category: Category.Filesystem,
            requiresElevation: true,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RawDiskC },
            estimatedMemoryMB: 64,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            if (ctx.Elevation != Forens.Common.Host.ElevationState.Elevated)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "USN journal access requires administrator + SeBackupPrivilege (CreateFile on \\\\.\\<volume>)");
            }
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string volumePath = SystemVolumePath();
            NtfsVolumeReader reader;
            try { reader = NtfsVolumeReader.Open(volumePath); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                writer.RecordPartial("CreateFile " + volumePath + " denied — needs admin + SeBackupPrivilege");
                return;
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Cannot open volume {Path}", volumePath);
                writer.RecordPartial("Cannot open volume " + volumePath + ": " + ex.Message);
                return;
            }

            using (reader)
            using (var headerJl = writer.OpenJsonlFile("usn-journal-header.jsonl"))
            using (var jl = writer.OpenJsonlFile("usn-journal.jsonl"))
            {
                UsnJournalData header;
                try { header = reader.QueryUsnJournal(); }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "QueryUsnJournal failed");
                    writer.RecordPartial("QueryUsnJournal failed: " + ex.Message);
                    return;
                }

                headerJl.Write(new JournalHeaderRecord
                {
                    VolumePath = volumePath,
                    JournalId = "0x" + header.UsnJournalId.ToString("X16"),
                    FirstUsn = header.FirstUsn,
                    NextUsn = header.NextUsn,
                    LowestValidUsn = header.LowestValidUsn,
                    MaxUsn = header.MaxUsn,
                    MaximumSizeBytes = header.MaximumSize,
                    AllocationDeltaBytes = header.AllocationDelta
                });
                writer.RecordItem();

                long emitted = 0;
                bool capped = false;
                try
                {
                    foreach (var rec in reader.EnumUsnData(header.LowestValidUsn, header.NextUsn))
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        if (emitted >= MaxRecords) { capped = true; break; }
                        jl.Write(new UsnRecordOutput
                        {
                            FileReferenceNumber = "0x" + rec.FileReferenceNumber.ToString("X16"),
                            ParentFileReferenceNumber = "0x" + rec.ParentFileReferenceNumber.ToString("X16"),
                            Usn = rec.Usn,
                            TimeStampUtc = rec.TimeStampUtc,
                            ReasonHex = "0x" + rec.Reason.ToString("X8"),
                            ReasonDecoded = rec.ReasonDecoded,
                            FileAttributesHex = "0x" + rec.FileAttributes.ToString("X8"),
                            FileAttributesDecoded = rec.FileAttributesDecoded,
                            FileName = rec.FileName
                        });
                        writer.RecordItem();
                        emitted++;
                    }
                }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "USN enumeration failed mid-iteration");
                    writer.RecordPartial("USN enumeration failed mid-iteration: " + ex.Message);
                }

                if (capped)
                {
                    writer.RecordPartial("USN output capped at " + MaxRecords + " records to bound disk usage");
                }
            }
        }

        private static string SystemVolumePath()
        {
            // The system drive is wherever %SystemDrive% points (typically C:).
            string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            // Trim trailing separator and use \\.\X: form.
            drive = drive.TrimEnd('\\', '/');
            return @"\\.\" + drive;
        }

        private sealed class JournalHeaderRecord
        {
            public string VolumePath { get; set; }
            public string JournalId { get; set; }
            public long FirstUsn { get; set; }
            public long NextUsn { get; set; }
            public long LowestValidUsn { get; set; }
            public long MaxUsn { get; set; }
            public ulong MaximumSizeBytes { get; set; }
            public ulong AllocationDeltaBytes { get; set; }
        }

        private sealed class UsnRecordOutput
        {
            public string FileReferenceNumber { get; set; }
            public string ParentFileReferenceNumber { get; set; }
            public long Usn { get; set; }
            public DateTimeOffset? TimeStampUtc { get; set; }
            public string ReasonHex { get; set; }
            public string ReasonDecoded { get; set; }
            public string FileAttributesHex { get; set; }
            public string FileAttributesDecoded { get; set; }
            public string FileName { get; set; }
        }
    }
}
