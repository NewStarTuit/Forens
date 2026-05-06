using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Forens.Common.Host;
using Forens.Core.Collection.Run;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Forens.Core.Collection
{
    public static class ManifestBuilder
    {
        public const string SchemaName = "forens-manifest";
        public const string SchemaVersion = "1.0.0";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffK",
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static string SerializeToJson(CollectionRun run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var dto = ToDto(run);
            return JsonConvert.SerializeObject(dto, JsonSettings);
        }

        public static void WriteToFile(CollectionRun run, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required", nameof(path));
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, SerializeToJson(run), new UTF8Encoding(false));
        }

        private static ManifestDto ToDto(CollectionRun r)
        {
            return new ManifestDto
            {
                Schema = new SchemaDto { Name = SchemaName, Version = SchemaVersion },
                RunId = r.RunId.ToString("D"),
                Tool = new ToolDto
                {
                    Name = "forens",
                    Version = r.ToolVersion,
                    TargetFramework = "net462",
                    GitCommit = r.GitCommit ?? "unknown"
                },
                Host = new HostDto { Name = r.HostName, OsVersion = r.HostOsVersion },
                Operator = new OperatorDto
                {
                    Account = r.OperatorAccount,
                    Elevation = r.Elevation == ElevationState.Elevated ? "Elevated" : "NotElevated",
                    CaseId = string.IsNullOrEmpty(r.CaseId) ? null : r.CaseId
                },
                StartedUtc = r.StartedUtc,
                CompletedUtc = r.CompletedUtc ?? r.StartedUtc,
                Status = r.Status.ToString(),
                StatusReason = r.StatusReason,
                Request = new RequestDto
                {
                    Profile = r.ProfileName,
                    Sources = r.RequestedSources,
                    TimeFrom = r.TimeFrom,
                    TimeTo = r.TimeTo,
                    ProcessFilter = ToProcessFilterDto(r.ProcessFilter),
                    Cli = r.Cli != null && r.Cli.Count > 0 ? r.Cli : null
                },
                Results = r.Results.Select(ToResultDto).ToList()
            };
        }

        private static ProcessFilterDto ToProcessFilterDto(ProcessFilterCriteria pf)
        {
            if (pf == null || pf.IsEmpty) return null;
            return new ProcessFilterDto
            {
                Pids = pf.Pids.Count > 0 ? pf.Pids.ToList() : null,
                ProcessNames = pf.ProcessNames.Count > 0 ? pf.ProcessNames.ToList() : null,
                ResolvedImagePaths = pf.ResolvedImagePaths.Count > 0 ? pf.ResolvedImagePaths.ToList() : null
            };
        }

        private static SourceResultDto ToResultDto(SourceResult r)
        {
            return new SourceResultDto
            {
                SourceId = r.SourceId,
                Status = r.Status.ToString(),
                StatusReason = r.StatusReason,
                StartedUtc = r.StartedUtc,
                CompletedUtc = r.CompletedUtc,
                ElapsedMs = r.ElapsedMs,
                ItemsCollected = r.Status == SourceStatus.Skipped ? (long?)null : r.ItemsCollected,
                BytesWritten = r.Status == SourceStatus.Skipped ? (long?)null : r.BytesWritten,
                OutputFiles = r.OutputFiles.Select(of => new OutputFileDto
                {
                    RelativePath = of.RelativePath,
                    Sha256 = of.Sha256,
                    ByteCount = of.ByteCount,
                    WrittenUtc = of.WrittenUtc
                }).ToList(),
                Error = r.Error == null ? null : new ErrorDto
                {
                    Type = r.Error.Type,
                    Message = r.Error.Message,
                    StackTrace = r.Error.StackTrace
                }
            };
        }

        // --- DTOs (camelCase via contract resolver) ---

        private sealed class ManifestDto
        {
            public SchemaDto Schema { get; set; }
            public string RunId { get; set; }
            public ToolDto Tool { get; set; }
            public HostDto Host { get; set; }
            public OperatorDto Operator { get; set; }
            public DateTimeOffset StartedUtc { get; set; }
            public DateTimeOffset CompletedUtc { get; set; }
            public string Status { get; set; }
            public string StatusReason { get; set; }
            public RequestDto Request { get; set; }
            public List<SourceResultDto> Results { get; set; }
        }

        private sealed class SchemaDto
        {
            public string Name { get; set; }
            public string Version { get; set; }
        }

        private sealed class ToolDto
        {
            public string Name { get; set; }
            public string Version { get; set; }
            public string TargetFramework { get; set; }
            public string GitCommit { get; set; }
        }

        private sealed class HostDto
        {
            public string Name { get; set; }
            public string OsVersion { get; set; }
        }

        private sealed class OperatorDto
        {
            public string Account { get; set; }
            public string Elevation { get; set; }
            public string CaseId { get; set; }
        }

        private sealed class RequestDto
        {
            public string Profile { get; set; }
            public IReadOnlyList<string> Sources { get; set; }
            public DateTimeOffset? TimeFrom { get; set; }
            public DateTimeOffset? TimeTo { get; set; }
            public ProcessFilterDto ProcessFilter { get; set; }
            public IReadOnlyList<string> Cli { get; set; }
        }

        private sealed class ProcessFilterDto
        {
            public List<int> Pids { get; set; }
            public List<string> ProcessNames { get; set; }
            public List<string> ResolvedImagePaths { get; set; }
        }

        private sealed class SourceResultDto
        {
            public string SourceId { get; set; }
            public string Status { get; set; }
            public string StatusReason { get; set; }
            public DateTimeOffset StartedUtc { get; set; }
            public DateTimeOffset CompletedUtc { get; set; }
            public long ElapsedMs { get; set; }
            public long? ItemsCollected { get; set; }
            public long? BytesWritten { get; set; }
            public List<OutputFileDto> OutputFiles { get; set; }
            public ErrorDto Error { get; set; }
        }

        private sealed class OutputFileDto
        {
            public string RelativePath { get; set; }
            public string Sha256 { get; set; }
            public long ByteCount { get; set; }
            public DateTimeOffset WrittenUtc { get; set; }
        }

        private sealed class ErrorDto
        {
            public string Type { get; set; }
            public string Message { get; set; }
            public string StackTrace { get; set; }
        }
    }
}
