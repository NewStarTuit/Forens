using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Forens.Core.Collectors.Hive
{
    /// <summary>
    /// Loads an offline Windows registry hive file (.hve / SAM / SECURITY / etc.) into
    /// a private process namespace via RegLoadAppKey, returning a normal RegistryKey.
    /// Used by Amcache (chunk 6), and reused by SAM/SECURITY hive sources (chunk 9+).
    /// </summary>
    public static class HiveLoader
    {
        private const int KEY_READ = 0x20019;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegLoadAppKey(
            string hiveFile, out IntPtr hKey, int samDesired, int options, int reserved);

        public static RegistryKey Open(string hivePath)
        {
            IntPtr hKey;
            int rc = RegLoadAppKey(hivePath, out hKey, KEY_READ, 0, 0);
            if (rc != 0)
            {
                throw new System.ComponentModel.Win32Exception(rc);
            }
            var safeHandle = new SafeRegistryHandle(hKey, ownsHandle: true);
            return RegistryKey.FromHandle(safeHandle);
        }
    }
}
