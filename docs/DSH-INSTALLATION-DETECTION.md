# DSH installation detection contract

The detection layer exposes all validated installations for the installer UI to
select explicitly. It does not select a migration source or modify installations.

```csharp
List<DshInstallation> candidates = DshLocator.FindInstallations(rootHint);
List<DshInstallation> candidatesFromProbe = report.DshInstallations;

// DshInstallationKind.Npm or DshInstallationKind.OfficialDesktop
DshInstallation selected = candidates[0];
string sourceRoot = selected.Root;
string sourceVersion = selected.Version; // null when unavailable
string sourceDataHome = selected.DataHome;
string cliEntry = selected.CliPath;

bool isOfficial = DshLocator.IsOfficialDesktopRoot(path);
bool isRunning = DshLocator.IsRootRunning(sourceRoot);
```

All types are in `DshInstaller.Shared`. `FindInstallations(string rootHint = null)`
returns a new list with the validated hint first, followed by ordinary discovery
and official uninstall entries. Roots are absolute, normalized and deduplicated
without regard to casing or trailing directory separators. No candidate is
automatically chosen for migration, and multiple ordinary and official roots
remain in the list.

| Member | Npm | OfficialDesktop |
| --- | --- | --- |
| `Root` | npm installation root | Electron installation root; migration source only |
| `Version` | Top-level DSH package version | Uninstall `DisplayVersion`, then bundled CLI package version |
| `DataHome` | `<Root>/.dsh` | Current Windows user's profile directory plus `.dsh` |
| `CliPath` | `node_modules/@deepseek-ai/dsh/lib/bin.js` | `resources/runtime/cli/bin/dsh.cmd` |

`DataHome` describes the existing default layout, without creating the directory
or resolving a custom `DSH_HOME` override. Elevated execution under a different
Windows account reports that account's profile. The caller remains responsible
for choosing the actual source data directory when defaults have been overridden.

Official discovery reads `SOFTWARE/Microsoft/Windows/CurrentVersion/Uninstall`
in both HKCU and HKLM, using explicit Registry32 and Registry64 views. It accepts
the complete display name `DeepSeek Harness`, optionally followed by one space
and a valid semantic version, such as `DeepSeek Harness 0.2.0-rc.2` (case
insensitive, outer whitespace ignored). Version suffixes require three numeric
components and may include valid prerelease/build identifiers. Other product
suffixes, leading-zero numeric versions and trailing text are rejected.
Paths come from `InstallLocation`, then the directory of
`DisplayIcon`, then the executable directory of `UninstallString`. Environment
variables, quoted paths, icon indices and executable arguments are handled
without executing registry commands. Inaccessible entries do not hide other
entries or stores. No official installation directory is hardcoded.

Each official root must contain both real files:

- `resources/runtime/cli/bin/dsh.cmd`
- `resources/app.asar`

A same-name Chrome PWA or a partial desktop package does not qualify. A root
with only one desktop marker is also excluded from ordinary reuse even if it
contains the npm marker. Structural validation establishes the installation
layout; it does not verify publisher signatures or execute the CLI.

`IsOfficialDesktopRoot(path)` exposes the same two-file validation for target
guards. For protection of targets nested inside an official root, the caller
must also check ancestor directories. `IsRootRunning(root)` checks official
roots against executable paths from a completed CIM process scan. Any executable
inside that root (Electron, renderer, bundled Node or other helper) marks the
source busy; a similarly named sibling root does not. Incomplete/failed process
scans throw `IOException`, so migration must not interpret them as stopped.
Process state can change after the check; the caller owns the migration lifecycle.

Compatibility: `FindCandidates()`, `LooksLikeDshRoot()` and `ReadPackageVersion()`
retain their ordinary-install meaning. `report["dsh"]` selects the first npm
candidate, even when an official hint is first in the full list. Official-only
detection leaves this legacy component `Missing`, allowing installation of a
new npm target. No migration or UI behavior is implemented in this layer.

Focused regression project: `tools/DshLocatorRegression`. Its executable takes
one absolute sandbox parent path; pass the workspace's `Temp_SetupandLauncher`.
An optional second argument names an expected real official root and enables
discovery without a root hint against the host's uninstall registration. The
expected path is a test argument, never a production discovery constant.
All fixtures are created beneath a unique child and deleted on completion.
It covers layouts, registry metadata parsing, PWA rejection, multiple roots,
normalization, version priority, legacy report behavior, process matching and
a real CIM scan against a temporary renamed executable. It does not install,
uninstall, write uninstall keys, or test real Electron migration.

The same regression also covers Windows App Runtime filtering aligned with Boot:
only complete package identities with a four-part version qualify; x64 is
accepted for both known 1.8 package prefixes, and neutral is additionally accepted
for the Main prefix. x86, ARM64 and neutral framework packages are rejected.
HKLM and HKCU Runtime stores are read independently. Failure opening or enumerating
one store is logged and does not discard the other store's valid result. The
highest compatible version across both stores is returned.
