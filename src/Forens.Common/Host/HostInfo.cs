using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Forens.Common.Host
{
    public static class HostInfo
    {
        public static string MachineName
        {
            get { return Environment.MachineName; }
        }

        public static string OperatorAccount
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return identity.Name;
                }
            }
        }

        public static ElevationState Elevation
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator)
                        ? ElevationState.Elevated
                        : ElevationState.NotElevated;
                }
            }
        }

        public static Version OsVersion
        {
            get
            {
                var info = new RTL_OSVERSIONINFOEX { dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEX)) };
                if (RtlGetVersion(ref info) == 0)
                {
                    return new Version(
                        (int)info.dwMajorVersion,
                        (int)info.dwMinorVersion,
                        (int)info.dwBuildNumber);
                }
                return Environment.OSVersion.Version;
            }
        }

        public static string OsVersionString
        {
            get
            {
                var v = OsVersion;
                return string.Format("Windows {0}.{1}.{2}", v.Major, v.Minor, v.Build);
            }
        }

        [DllImport("ntdll.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX versionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RTL_OSVERSIONINFOEX
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }
    }
}
