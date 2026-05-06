using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Forens.Integration.Tests.Cli
{
    public sealed class CliResult
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;
    }

    public static class CliRunner
    {
        public static string ResolveForensExe()
        {
            string testAsmDir = Path.GetDirectoryName(typeof(CliRunner).Assembly.Location);
            string candidate = Path.Combine(testAsmDir, "forens.exe");
            if (File.Exists(candidate)) return candidate;

            // Fall back: walk up the test bin to find Forens.Cli's bin dir.
            string dir = testAsmDir;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string sibling = Path.Combine(dir, "src", "Forens.Cli", "bin", "Release", "forens.exe");
                if (File.Exists(sibling)) return sibling;
                sibling = Path.Combine(dir, "src", "Forens.Cli", "bin", "Debug", "forens.exe");
                if (File.Exists(sibling)) return sibling;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new FileNotFoundException("Could not locate forens.exe near " + testAsmDir);
        }

        public static CliResult Run(params string[] args)
        {
            string exe = ResolveForensExe();
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList_AddSafe(a);

            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120_000);
                return new CliResult
                {
                    ExitCode = p.ExitCode,
                    StdOut = stdout,
                    StdErr = stderr
                };
            }
        }
    }

    internal static class ProcessStartInfoExtensions
    {
        // .NET Framework 4.6.2's ProcessStartInfo.ArgumentList does not exist; we build
        // a properly-quoted Arguments string instead.
        public static void ArgumentList_AddSafe(this ProcessStartInfo psi, string arg)
        {
            string quoted = QuoteForWindows(arg);
            psi.Arguments = string.IsNullOrEmpty(psi.Arguments) ? quoted : psi.Arguments + " " + quoted;
        }

        private static string QuoteForWindows(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"', '\\' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; }
                else if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                    backslashes = 0;
                }
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
