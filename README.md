# Forens — Windows Artifacts Collection Tool

> **One command, 40 forensic sources, full chain-of-custody.** A modular,
> read-only Windows forensic collector targeting .NET Framework 4.6.2
> (runs unchanged on Windows 7 SP1 through Windows 11 24H2).

```cmd
forens collect --output C:\Investigations\Case42 --keyword "powershell.exe" --keyword "bypass"
```

→ Collects from every applicable artifact source, hashes every output,
produces a self-contained HTML report, and aggregates records mentioning
your keywords — all in a single command, in under a minute.

---

## What it does

Forens is an incident-response and forensic-acquisition tool for Windows
hosts. Drop the deployment folder onto a target host, run one command,
and walk away with:

- A timestamped run directory containing **40 forensic artifact sources**
  (event logs, registry persistence, prefetch, autoruns, scheduled tasks,
  network state, browser bookmarks, USB history, Defender history, MFT,
  USN journal, and ~30 more).
- A **chain-of-custody manifest** with SHA-256 of every output file,
  tool version, git commit, host info, operator account, elevation
  state, exact CLI argv, per-source status with reason.
- A **self-contained HTML report** (no external CDN/network at view
  time) for analyst triage.
- A **machine-readable JSON report** for downstream pipelines.
- A **structured run.log** in Compact JSON.

If you supply `--keyword "X"`, the tool also walks every output
line-by-line and emits a unified `search.jsonl` of records mentioning
any of your keywords, with full provenance (which source, which file,
which line).

**Designed for**: Incident responders, forensic analysts, blue-team
DFIR. Not a hacking tool — strictly read-only on the target host, no
persistence, no installation.

---

## Quick start

```cmd
:: 1. Build (or copy a prebuilt deployment folder)
dotnet build Forens.slnx -c Release

:: 2. Drop the binary onto the target host (single-folder deploy)
:: 3. Run — defaults to live-triage (34 unprivileged sources)
.\src\Forens.Cli\bin\Release\forens.exe collect --output .\out

:: 4. Open the report in a browser
start .\out\forens-<host>-<utc>\report.html

:: 5. Verify integrity
powershell -ExecutionPolicy Bypass -File examples\verify-integrity.ps1 ^
    -RunDir .\out\forens-<host>-<utc>
```

---

## Features

- **40 artifact sources** across 8 forensic categories (Persistence,
  System, User, Network, Filesystem, Browser, Process, Other). New
  sources are one file each — drop a class implementing
  `IArtifactSource` next to the others, the tool finds it by reflection.
- **3 named profiles** with auto-tuned resource budgets:
  - `live-triage` — 34 unprivileged sources, 256 MB memory ceiling, ≤2 parallel
  - `default` — 38 sources (no MFT/USN), 512 MB ceiling, ≤8 parallel
  - `full` — all 40 sources including raw NTFS, 1024 MB ceiling
- **Drop-and-run**: `forens collect --output ./out` works without any
  other flags; defaults to live-triage.
- **Inline keyword search**: `--keyword X` runs collection + cross-source
  aggregation in one shot. OR-logic with multiple keywords, case-insensitive
  by default.
- **Time scoping** for event-log sources via `--from`/`--to` (strict
  ISO-8601 with offset).
- **Process scoping** via `--pid` and `--process-name` honored by every
  source that has process attribution (live PIDs and historical image
  paths).
- **Graceful elevation handling**: every source that needs admin or
  `SeBackupPrivilege` skips with a specific reason on a non-admin shell.
  No source fails the run because of permissions.
- **Streaming output** with in-line SHA-256: a single 60 MB event-log
  blob is written and hashed in one pass, never buffered in memory.
- **No password hashes, no LSA secret values, no cookie values** —
  enforced by reflection tests on every hive-parsing source. The tool
  is forensically useful but cannot exfiltrate credentials.
- **Single-folder deployment**: zero installation, zero registration,
  zero registry footprint. Drop and run.

---

## Requirements

| | Requirement |
|---|---|
| **OS** | Windows 7 SP1+ / Windows Server 2008 R2+. Verified end-to-end on Windows 11 24H2 (build 26200). |
| **Runtime** | .NET Framework 4.6.2 or later. Modern Windows 10/11 ships with 4.6.2 by default. |
| **Privileges** | Not required for `live-triage` profile (34 of 40 sources). Run elevated to add 6 more (`eventlog-security`, `shimcache`, `registry-sam`, `registry-security`, `mft-metadata`, `usn-journal`). The last two also require `SeBackupPrivilege`. |
| **Disk** | The default profile budgets 1 GB free on the output volume; the watchdog stops the run cleanly if it drops below. |
| **Build prerequisites** | .NET SDK with the .NET Framework 4.6.2 targeting pack installed (Visual Studio "Desktop development with .NET" workload, or `Microsoft.NETFramework.ReferenceAssemblies` NuGet which is already pinned in this repo). |

No external NuGet binaries to deploy alongside `forens.exe` — every
dependency is a managed-only DLL or pinned to .NET Framework 4.6.2's
shipped surface (`System.Management`, `System.Net.NetworkInformation`,
`System.Diagnostics.Eventing.Reader`).

---

## Usage

### Collect everything (zero-config)

```cmd
forens collect --output C:\Investigations\Case42 --case-id INC-2026-117
```

### Collect everything + search for keywords in one shot

```cmd
forens collect --output C:\Investigations\Case42 ^
    --keyword "cmd.exe" --keyword "bypass" --keyword "C:\\Users\\victim"
```

### Targeted single source

```cmd
forens collect --sources process-list --output ./out
```

### Multiple sources

```cmd
forens collect --sources autoruns,scheduled-tasks,services,prefetch --output ./out
```

### Profile-driven

```cmd
forens collect --profile live-triage --output ./out   # 34 sources, default
forens collect --profile default     --output ./out   # 38 (admin recommended)
forens collect --profile full        --output ./out   # 40 (admin + SeBackup)
```

### Time-range scoping (event-log sources)

```cmd
forens collect --profile live-triage ^
    --from 2026-05-01T00:00:00Z --to 2026-05-06T23:59:59Z ^
    --output ./out
```

### Process scoping

```cmd
forens collect --profile live-triage --process-name chrome.exe --output ./out
forens collect --profile live-triage --pid 1234,5678          --output ./out
```

### Search an existing run

```cmd
forens search --run C:\Investigations\Case42\forens-<host>-<utc> ^
    --keyword chrome.exe
```

### List the catalog

```cmd
forens list                # human-readable, 40 rows
forens list --json         # JSON array
forens list --profiles     # the 3 profiles + their resolved source lists
```

### Preview without collecting

```cmd
forens collect --output ./out --dry-run
```

For the full flag reference (every flag, every exit code, every output
schema), see [`docs/USAGE.md`](docs/USAGE.md).

---

## Result examples

### Run directory layout

```
forens-DESKTOP-S817L4K-2026-05-06T07-20-17Z/
├── manifest.json              # chain-of-custody (versioned schema)
├── report.json                # analyst report (versioned schema)
├── report.html                # self-contained, no network resources
├── run.log                    # Serilog CompactJsonFormatter
├── search.jsonl               # (when --keyword supplied) per-hit records
├── search.summary.json        # (when --keyword supplied) per-source counts
└── raw/
    ├── amcache/amcache.jsonl
    ├── autoruns/autoruns.jsonl
    ├── eventlog-application/events.jsonl
    ├── eventlog-defender/events.jsonl
    ├── eventlog-system/events.jsonl       (~60 MB, 60k+ events on a busy host)
    ├── prefetch/prefetch.jsonl            (full binary parse — see below)
    ├── process-list/processes.jsonl
    └── ... (one folder per source)
```

### Console output of `forens collect --output ./out --keyword "cmd.exe" --keyword "bypass"`

```
(no --sources / --profile given; defaulting to --profile live-triage)
forens collect — pre-run summary
   Profile: live-triage  (34 sources)
   Run id: 4d98e44a-117e-433d-abc5-6026e732d3d6
   Output dir: forens-DESKTOP-S817L4K-2026-05-06T07-20-17Z
   Filters: (none)
   Elevation: NotElevated
   Sources to skip at this privilege level (1):
     eventlog-sysmon              — Event log channel not found: Microsoft-Windows-Sysmon/Operational
   Sources to run (33): ...

Run 4d98e44a-117e-433d-abc5-6026e732d3d6 CompletedWithErrors: 27 succeeded, 5 partial, 2 skipped, 0 failed
Output: ./out\forens-DESKTOP-S817L4K-2026-05-06T07-20-17Z

forens search (cmd.exe,bypass): 208 matches across 8 sources in 34 files (75140 lines scanned)
  output: ./out\forens-DESKTOP-S817L4K-2026-05-06T07-20-17Z\search.jsonl
  summary: ./out\forens-DESKTOP-S817L4K-2026-05-06T07-20-17Z\search.summary.json
```

### Sample record — fully-parsed Prefetch (executable-execution evidence)

```json
{
  "fileName": "ACCOUNTSCONTROLHOST.EXE-F6741AD7.pf",
  "formatVersion": 31,
  "wasCompressed": true,
  "executableName": "ACCOUNTSCONTROLHOST.EXE",
  "executableFullPath": "\\VOLUME{01dca77240919d20-5a409b83}\\WINDOWS\\SYSTEMAPPS\\MICROSOFT.ACCOUNTSCONTROL_CW5N1H2TXYEWY\\ACCOUNTSCONTROLHOST.EXE",
  "pathHash": "F6741AD7",
  "runCount": 7,
  "lastRunTimesUtc": ["2026-04-21T01:34:40.822+00:00"],
  "volumeCount": 1,
  "referencedFileCount": 142,
  "referencedFiles": ["\\VOLUME{...}\\WINDOWS\\SYSTEM32\\NTDLL.DLL", "..."]
}
```

### Sample record — fully-parsed LNK target

```json
{
  "lnkPath": "C:\\Users\\blade\\Desktop\\Cursor.lnk",
  "headerValid": true,
  "localBasePath": "C:\\Users\\blade\\AppData\\Local\\Programs\\cursor\\Cursor.exe",
  "driveType": 3,
  "driveTypeName": "DRIVE_FIXED",
  "driveSerialHex": "0x5A409B83",
  "targetFileSize": 211042088,
  "targetCreationUtc": "2026-04-24T01:33:29.122+00:00",
  "targetWriteUtc":    "2026-04-19T12:00:30.000+00:00",
  "workingDir": "C:\\Users\\blade\\AppData\\Local\\Programs\\cursor"
}
```

### Sample `search.summary.json` (cmd.exe + bypass)

```json
{
  "caseSensitive": false,
  "keywords": ["cmd.exe", "bypass"],
  "filesScanned": 34,
  "totalLinesScanned": 75140,
  "totalMatches": 208,
  "matchesPerSource": {
    "eventlog-system":     100,
    "eventlog-powershell":  72,
    "lnk-targets":          18,
    "prefetch":              6,
    "process-list":          6,
    "scheduled-tasks":       3,
    "environment":           2,
    "userassist":            1
  }
}
```

### Sample `manifest.json` excerpt

```json
{
  "schema":   { "name": "forens-manifest", "version": "1.0.0" },
  "tool":     { "name": "forens", "version": "0.1.0+...", "targetFramework": "net462", "gitCommit": "..." },
  "host":     { "name": "DESKTOP-S817L4K", "osVersion": "Windows 10.0.26200" },
  "operator": { "account": "DESKTOP-S817L4K\\blade", "elevation": "NotElevated" },
  "startedUtc":   "2026-05-06T07:20:17.798+00:00",
  "completedUtc": "2026-05-06T07:20:58.946+00:00",
  "status": "CompletedWithErrors",
  "request": {
    "profile": "live-triage",
    "sources": ["amcache", "autoruns", "..."],
    "cli": ["forens", "collect", "--output", "./out", "--keyword", "cmd.exe", "--keyword", "bypass"]
  },
  "results": [
    {
      "sourceId": "eventlog-system",
      "status": "Partial",
      "statusReason": "One or more events could not be rendered",
      "itemsCollected": 62435,
      "bytesWritten": 61965933,
      "outputFiles": [{
        "relativePath": "raw/eventlog-system/events.jsonl",
        "sha256": "a6eaaaf22a770b477eb897dcbf4d37d0...",
        "byteCount": 61965933,
        "writtenUtc": "2026-05-06T07:20:58.944+00:00"
      }]
    },
    "..."
  ]
}
```

### Real numbers from a Windows 11 24H2 host running `forens collect --profile live-triage`

| Metric | Value |
|--------|-------|
| Sources executed | 34 |
| Succeeded | 27 |
| Partial | 5 (high-volume event logs, ACL-restricted Defender direct root, 1 corrupt .pf, VSS non-admin) |
| Skipped | 2 (Amcache.hve ACL, Sysmon channel not installed) |
| Failed | 0 |
| Total wall-clock time | ~40 seconds |
| Total output volume | ~78 MB across 34 raw files |
| Largest source | `eventlog-system` (60 MB, 62k+ events) |
| Integrity verification | All 34 files SHA-256 verified |

---

## Catalog overview (40 sources)

| Category | Sources |
|----------|---------|
| **Persistence** (14) | services, autoruns, scheduled-tasks, prefetch, amcache, eventlog-{defender,security,applocker,powershell,sysmon}, wmi-startup-commands, shimcache, defender-exclusions, registry-security |
| **System** (9) | system-info, installed-software, environment, eventlog-{application,system,setup}, usb-history, lnk-files, windows-updates |
| **User** (6) | userassist, users-groups, runmru, recentdocs, shellbags, lnk-targets, registry-sam (when elevated) |
| **Filesystem** (4) | recycle-bin, vss-snapshots, mft-metadata, usn-journal |
| **Network** (3) | network-config, network-connections, eventlog-rdp |
| **Browser** (1) | browser-bookmarks |
| **Process** (1) | process-list |
| **Other** (1) | defender-detection-history (file-based MPLog parser) |

Each source has a stable kebab-case id, declares its capabilities
(elevation requirement, time-scope support, process-scope support,
contended resources) in `IArtifactSource.Metadata`, and is discovered
at startup via reflection.

---

## Build from source

```cmd
git clone <repo-url>
cd forens
dotnet restore Forens.slnx
dotnet build Forens.slnx -c Release
dotnet test Forens.slnx -c Release
```

Output binary: `src\Forens.Cli\bin\Release\forens.exe`.

The build pins .NET Framework 4.6.2 via `Directory.Build.props`. With
the .NET 4.6.2 Developer Pack installed (or via the
`Microsoft.NETFramework.ReferenceAssemblies` NuGet, which the build
pulls automatically), the tree builds on any Windows host with the
.NET SDK or Visual Studio.

### Test counts (current main)

- Forens.Core.Tests: 225 unit tests (parsers, primitives, scheduler, runner, every collector's metadata)
- Forens.Reporting.Tests: 7 (HTML + JSON report contracts)
- Forens.Integration.Tests: 23 (subprocess-based CLI contract tests)
- **Total: 255 tests, all passing**

---


## Project principles (excerpt from constitution)

1. **Forensic Integrity (NON-NEGOTIABLE)** — read-only on the target.
   Every output file is hashed (SHA-256) at write time. Any source that
   could modify the host is opt-in and self-declares the modification.
2. **Comprehensive Artifact Coverage** — the catalog covers every named
   Windows artifact category. Adding new sources is one file.
3. **Time & Process Scoping (NON-NEGOTIABLE)** — every source supports
   `--from/--to` and `--pid/--process-name` filters or declares the
   limitation.
4. **Dual-Format Reporting** — JSON + HTML produced from one in-memory
   model. HTML is self-contained.
5. **Reproducibility & Chain of Custody** — `manifest.json` records
   tool version, git commit, host, operator, elevation, exact CLI argv,
   per-file SHA-256.
6. **Least Privilege & Explicit Elevation** — collectors prefer
   non-elevated execution; sources that need elevation declare it and
   skip cleanly when not granted.
7. **Platform Compatibility (.NET Framework 4.6.2)** — pinned to support
   the broadest Windows estate. Raising the target is a constitutional
   amendment.

---


## License

Choose a license appropriate to your context (MIT / Apache-2.0 are
common for forensic tooling). Add a `LICENSE` file before publishing.

---

## Acknowledgements

Built with [Spec Kit](https://github.com/github/spec-kit) for the
spec-driven development workflow. Thanks to the open-source forensic
community — Eric Zimmerman's tooling, libscca, MS-SHLLINK
documentation, and the [MS-SHLLINK] / winioctl.h references that made
the binary parsers possible.
