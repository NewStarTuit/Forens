using System;
using System.Linq;
using System.Reflection;
using McMaster.Extensions.CommandLineUtils;

namespace Forens.Cli.Commands
{
    [Command("version", Description = "Print tool version, target framework, and git commit.")]
    public sealed class VersionCommand
    {
        public int OnExecute(IConsole console)
        {
            var asm = typeof(VersionCommand).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string version = info != null ? info.InformationalVersion : "0.0.0";
            string gitCommit = ReadMetadata(asm, "GitCommit") ?? "unknown";
            string targetFramework = ReadMetadata(asm, "TargetFramework") ?? "net462";
            console.WriteLine(string.Format("forens {0} ({1}) on .NET Framework {2}",
                version, gitCommit, targetFramework));
            return 0;
        }

        internal static string ReadMetadata(Assembly asm, string key)
        {
            return asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                      .Where(a => string.Equals(a.Key, key, StringComparison.Ordinal))
                      .Select(a => a.Value)
                      .FirstOrDefault();
        }
    }
}
