using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Forens.Core.Collectors.Registry
{
    public sealed class RegistryValueRecord
    {
        public RegistryValueRecord(string hive, string keyPath, string valueName, RegistryValueKind kind, object value)
        {
            Hive = hive;
            KeyPath = keyPath;
            ValueName = valueName;
            Kind = kind;
            Value = value;
        }
        public string Hive { get; }
        public string KeyPath { get; }
        public string ValueName { get; }
        public RegistryValueKind Kind { get; }
        public object Value { get; }
    }

    public sealed class RegistrySubkeyRecord
    {
        public RegistrySubkeyRecord(string hive, string parentKeyPath, string subkeyName, IReadOnlyDictionary<string, object> values)
        {
            Hive = hive;
            ParentKeyPath = parentKeyPath;
            SubkeyName = subkeyName;
            Values = values;
        }
        public string Hive { get; }
        public string ParentKeyPath { get; }
        public string SubkeyName { get; }
        public IReadOnlyDictionary<string, object> Values { get; }

        public string FullKeyPath
        {
            get { return ParentKeyPath + "\\" + SubkeyName; }
        }

        public string GetString(string name)
        {
            object v;
            if (Values.TryGetValue(name, out v) && v != null) return v.ToString();
            return null;
        }
    }

    public static class RegistryReader
    {
        public static IEnumerable<RegistryValueRecord> EnumerateValues(
            RegistryHive hive, RegistryView view, string subKeyPath)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (var key = baseKey.OpenSubKey(subKeyPath, writable: false))
            {
                if (key == null) yield break;
                foreach (var name in key.GetValueNames())
                {
                    object value;
                    RegistryValueKind kind;
                    try
                    {
                        kind = key.GetValueKind(name);
                        value = key.GetValue(name);
                    }
                    catch
                    {
                        continue;
                    }
                    yield return new RegistryValueRecord(HiveLabel(hive), subKeyPath, name, kind, value);
                }
            }
        }

        public static IEnumerable<RegistrySubkeyRecord> EnumerateSubkeys(
            RegistryHive hive, RegistryView view, string subKeyPath)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (var key = baseKey.OpenSubKey(subKeyPath, writable: false))
            {
                if (key == null) yield break;
                string[] subNames;
                try { subNames = key.GetSubKeyNames(); }
                catch { yield break; }

                foreach (var subName in subNames)
                {
                    Dictionary<string, object> values;
                    try
                    {
                        using (var sub = key.OpenSubKey(subName, writable: false))
                        {
                            if (sub == null) continue;
                            values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var n in sub.GetValueNames())
                            {
                                try { values[n] = sub.GetValue(n); }
                                catch { /* skip unreadable */ }
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                    yield return new RegistrySubkeyRecord(HiveLabel(hive), subKeyPath, subName, values);
                }
            }
        }

        private static string HiveLabel(RegistryHive hive)
        {
            switch (hive)
            {
                case RegistryHive.LocalMachine: return "HKLM";
                case RegistryHive.CurrentUser: return "HKCU";
                case RegistryHive.Users: return "HKU";
                case RegistryHive.ClassesRoot: return "HKCR";
                case RegistryHive.CurrentConfig: return "HKCC";
                default: return hive.ToString();
            }
        }
    }
}
