using System;
using System.Collections.Generic;
using Forens.Core.Collection;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class UsbHistorySource : IArtifactSource
    {
        public const string SourceId = "usb-history";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "USB Device History",
            description: "USB devices ever attached (HKLM\\SYSTEM\\CurrentControlSet\\Enum\\USBSTOR + Enum\\USB).",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveSystem },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("usb-history.jsonl"))
            {
                EmitFromRoot(@"SYSTEM\CurrentControlSet\Enum\USBSTOR", "USBSTOR", ctx, writer, jl);
                EmitFromRoot(@"SYSTEM\CurrentControlSet\Enum\USB", "USB", ctx, writer, jl);
            }
        }

        private static void EmitFromRoot(string rootPath, string sourceCategory, CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var root = baseKey.OpenSubKey(rootPath, writable: false))
            {
                if (root == null) return;
                string[] deviceClassNames;
                try { deviceClassNames = root.GetSubKeyNames(); }
                catch (Exception ex)
                {
                    ctx.Logger.Verbose(ex, "Failed to enumerate {Root}", rootPath);
                    writer.RecordPartial("Failed to enumerate USB registry root: " + rootPath);
                    return;
                }

                foreach (var deviceClass in deviceClassNames)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    using (var classKey = root.OpenSubKey(deviceClass, writable: false))
                    {
                        if (classKey == null) continue;
                        string[] instanceNames;
                        try { instanceNames = classKey.GetSubKeyNames(); }
                        catch { continue; }

                        foreach (var instance in instanceNames)
                        {
                            using (var instanceKey = classKey.OpenSubKey(instance, writable: false))
                            {
                                if (instanceKey == null) continue;

                                string friendlyName = SafeReadString(instanceKey, "FriendlyName");
                                string description = SafeReadString(instanceKey, "DeviceDesc");
                                string mfg = SafeReadString(instanceKey, "Mfg");
                                string service = SafeReadString(instanceKey, "Service");
                                string parentInstanceId = SafeReadString(instanceKey, "ParentIdPrefix");
                                string container = SafeReadString(instanceKey, "ContainerID");

                                jl.Write(new UsbRecord
                                {
                                    SourceCategory = sourceCategory,
                                    DeviceClass = deviceClass,
                                    InstanceId = instance,
                                    FriendlyName = friendlyName,
                                    Description = description,
                                    Manufacturer = mfg,
                                    Service = service,
                                    ParentIdPrefix = parentInstanceId,
                                    ContainerId = container
                                });
                                writer.RecordItem();
                            }
                        }
                    }
                }
            }
        }

        private static string SafeReadString(RegistryKey key, string name)
        {
            try { return key.GetValue(name) as string; }
            catch { return null; }
        }

        private sealed class UsbRecord
        {
            public string SourceCategory { get; set; }
            public string DeviceClass { get; set; }
            public string InstanceId { get; set; }
            public string FriendlyName { get; set; }
            public string Description { get; set; }
            public string Manufacturer { get; set; }
            public string Service { get; set; }
            public string ParentIdPrefix { get; set; }
            public string ContainerId { get; set; }
        }
    }
}
