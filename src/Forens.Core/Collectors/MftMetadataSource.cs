using System;
using System.ComponentModel;
using Forens.Core.Collection;
using Forens.Core.Collectors.Ntfs;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Enumerates the entire current MFT via FSCTL_ENUM_USN_DATA over the full
    /// [0..max] USN range, emitting one record per file/directory present in the
    /// MFT with FRN, ParentFRN, FileAttributes (decoded), latest USN, latest
    /// TimeStamp, and FileName. Analysts can post-process the FRN→ParentFRN
    /// graph to reconstruct the full directory tree without doing path
    /// resolution at collection time.
    ///
    /// Requires elevation + SeBackupPrivilege.
    /// </summary>
    public sealed class MftMetadataSource : IArtifactSource
    {
        public const string SourceId = "mft-metadata";
        private const int MaxRecords = 5_000_000;

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "MFT Metadata (file inventory)",
            description: "Full NTFS MFT inventory via FSCTL_ENUM_USN_DATA: per-file FRN, ParentFRN, attributes, last USN, last timestamp, filename.",
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
                    "MFT enumeration requires administrator + SeBackupPrivilege (CreateFile on \\\\.\\<volume>)");
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
            using (var jl = writer.OpenJsonlFile("mft-metadata.jsonl"))
            {
                long emitted = 0;
                bool capped = false;
                try
                {
                    foreach (var rec in reader.EnumUsnData(0, long.MaxValue))
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        if (emitted >= MaxRecords) { capped = true; break; }
                        jl.Write(new MftRecord
                        {
                            FileReferenceNumber = "0x" + rec.FileReferenceNumber.ToString("X16"),
                            ParentFileReferenceNumber = "0x" + rec.ParentFileReferenceNumber.ToString("X16"),
                            Usn = rec.Usn,
                            TimeStampUtc = rec.TimeStampUtc,
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
                    ctx.Logger.Warning(ex, "MFT enumeration failed mid-iteration");
                    writer.RecordPartial("MFT enumeration failed mid-iteration: " + ex.Message);
                }

                if (capped)
                {
                    writer.RecordPartial("MFT output capped at " + MaxRecords + " records to bound disk usage");
                }
            }
        }

        private static string SystemVolumePath()
        {
            string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            drive = drive.TrimEnd('\\', '/');
            return @"\\.\" + drive;
        }

        private sealed class MftRecord
        {
            public string FileReferenceNumber { get; set; }
            public string ParentFileReferenceNumber { get; set; }
            public long Usn { get; set; }
            public DateTimeOffset? TimeStampUtc { get; set; }
            public string FileAttributesHex { get; set; }
            public string FileAttributesDecoded { get; set; }
            public string FileName { get; set; }
        }
    }
}
