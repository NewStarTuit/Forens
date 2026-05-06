using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using Forens.Common.Host;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class SystemInfoSource : IArtifactSource
    {
        public const string SourceId = "system-info";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "System Information",
            description: "OS version, install date, hardware identity, BIOS, time zone, and last boot time.",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.WmiCimV2 },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            var record = new SystemInfoRecord
            {
                CapturedUtc = DateTimeOffset.UtcNow,
                MachineName = HostInfo.MachineName,
                UserDomainName = Environment.UserDomainName,
                UserName = Environment.UserName,
                OsVersion = HostInfo.OsVersionString,
                OsKernelVersion = HostInfo.OsVersion.ToString(),
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                Is64BitProcess = Environment.Is64BitProcess,
                ProcessorCount = Environment.ProcessorCount,
                SystemDirectory = Environment.SystemDirectory,
                CurrentDirectory = Environment.CurrentDirectory,
                TimeZoneId = TimeZoneInfo.Local.Id,
                TimeZoneDisplayName = TimeZoneInfo.Local.DisplayName,
                TimeZoneUtcOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes
            };

            FillFromWmi(record, ctx);

            using (var jl = writer.OpenJsonlFile("system-info.jsonl"))
            {
                jl.Write(record);
                writer.RecordItem();
            }
        }

        private static void FillFromWmi(SystemInfoRecord r, CollectionContext ctx)
        {
            QuerySingle(ctx, "SELECT Caption, Version, BuildNumber, InstallDate, RegisteredUser, OSArchitecture, LastBootUpTime, TotalVisibleMemorySize FROM Win32_OperatingSystem", mo =>
            {
                r.WindowsCaption = mo["Caption"] as string;
                r.WindowsVersion = mo["Version"] as string;
                r.WindowsBuildNumber = mo["BuildNumber"] as string;
                r.InstallDateUtc = ParseWmiDateTime(mo["InstallDate"] as string);
                r.LastBootUpTimeUtc = ParseWmiDateTime(mo["LastBootUpTime"] as string);
                r.RegisteredUser = mo["RegisteredUser"] as string;
                r.OSArchitecture = mo["OSArchitecture"] as string;
                r.TotalVisibleMemoryKb = ToLong(mo["TotalVisibleMemorySize"]);
            });

            QuerySingle(ctx, "SELECT Manufacturer, Model, Domain, Workgroup, TotalPhysicalMemory, NumberOfLogicalProcessors FROM Win32_ComputerSystem", mo =>
            {
                r.Manufacturer = mo["Manufacturer"] as string;
                r.Model = mo["Model"] as string;
                r.Domain = mo["Domain"] as string;
                r.Workgroup = mo["Workgroup"] as string;
                r.TotalPhysicalMemoryBytes = ToLong(mo["TotalPhysicalMemory"]);
                r.NumberOfLogicalProcessors = (int?)ToLong(mo["NumberOfLogicalProcessors"]);
            });

            QuerySingle(ctx, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS", mo =>
            {
                r.BiosManufacturer = mo["Manufacturer"] as string;
                r.BiosVersion = mo["SMBIOSBIOSVersion"] as string;
                r.BiosReleaseDateUtc = ParseWmiDateTime(mo["ReleaseDate"] as string);
                r.BiosSerialNumber = mo["SerialNumber"] as string;
            });

            QuerySingle(ctx, "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor", mo =>
            {
                r.ProcessorName = mo["Name"] as string;
                r.ProcessorCores = (int?)ToLong(mo["NumberOfCores"]);
                r.ProcessorLogicalProcessors = (int?)ToLong(mo["NumberOfLogicalProcessors"]);
                r.ProcessorMaxClockMhz = (int?)ToLong(mo["MaxClockSpeed"]);
            });
        }

        private static void QuerySingle(CollectionContext ctx, string wql, Action<ManagementBaseObject> apply)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(wql))
                using (var c = s.Get())
                {
                    foreach (ManagementBaseObject mo in c)
                    {
                        try { apply(mo); }
                        finally { mo.Dispose(); }
                        break; // first only
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "WMI query failed: {Wql}", wql);
            }
        }

        private static long ToLong(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        internal static DateTimeOffset? ParseWmiDateTime(string wmi)
        {
            if (string.IsNullOrEmpty(wmi) || wmi.Length < 21) return null;
            // CIM datetime: yyyymmddHHMMSS.mmmmmm+UUU
            try
            {
                int year = int.Parse(wmi.Substring(0, 4), CultureInfo.InvariantCulture);
                int month = int.Parse(wmi.Substring(4, 2), CultureInfo.InvariantCulture);
                int day = int.Parse(wmi.Substring(6, 2), CultureInfo.InvariantCulture);
                int hour = int.Parse(wmi.Substring(8, 2), CultureInfo.InvariantCulture);
                int minute = int.Parse(wmi.Substring(10, 2), CultureInfo.InvariantCulture);
                int second = int.Parse(wmi.Substring(12, 2), CultureInfo.InvariantCulture);
                int offset = 0;
                if (wmi.Length >= 25)
                {
                    char sign = wmi[21];
                    int mins = int.Parse(wmi.Substring(22, 3), CultureInfo.InvariantCulture);
                    offset = sign == '-' ? -mins : mins;
                }
                var dto = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(offset));
                return dto.ToUniversalTime();
            }
            catch { return null; }
        }

        private sealed class SystemInfoRecord
        {
            public DateTimeOffset CapturedUtc { get; set; }
            public string MachineName { get; set; }
            public string UserDomainName { get; set; }
            public string UserName { get; set; }
            public string Domain { get; set; }
            public string Workgroup { get; set; }
            public string OsVersion { get; set; }
            public string OsKernelVersion { get; set; }
            public string WindowsCaption { get; set; }
            public string WindowsVersion { get; set; }
            public string WindowsBuildNumber { get; set; }
            public string OSArchitecture { get; set; }
            public bool Is64BitOperatingSystem { get; set; }
            public bool Is64BitProcess { get; set; }
            public DateTimeOffset? InstallDateUtc { get; set; }
            public DateTimeOffset? LastBootUpTimeUtc { get; set; }
            public string RegisteredUser { get; set; }
            public string SystemDirectory { get; set; }
            public string CurrentDirectory { get; set; }
            public string TimeZoneId { get; set; }
            public string TimeZoneDisplayName { get; set; }
            public int TimeZoneUtcOffsetMinutes { get; set; }
            public string Manufacturer { get; set; }
            public string Model { get; set; }
            public long TotalPhysicalMemoryBytes { get; set; }
            public long TotalVisibleMemoryKb { get; set; }
            public int? NumberOfLogicalProcessors { get; set; }
            public int ProcessorCount { get; set; }
            public string ProcessorName { get; set; }
            public int? ProcessorCores { get; set; }
            public int? ProcessorLogicalProcessors { get; set; }
            public int? ProcessorMaxClockMhz { get; set; }
            public string BiosManufacturer { get; set; }
            public string BiosVersion { get; set; }
            public DateTimeOffset? BiosReleaseDateUtc { get; set; }
            public string BiosSerialNumber { get; set; }
        }
    }
}
