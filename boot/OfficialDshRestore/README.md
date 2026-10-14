# Official DSH Migration

The installer API is `OfficialDshExport.Export(root, home, chosen, outputExe, progress, log, token)`.
It returns `OfficialDshExportResult` with `Ok`, `Canceled`, `OutputPath`, `Error`, `Summary`, and `Warnings`.
Only `Ok` means a complete EXE was committed. Existing output is never replaced. Callback failure after
commit cannot turn a committed archive into a false failure or delete it.

The shared assembly embeds the compiled Framework WPF helper as
`DshInstaller.OfficialDshRestore.exe`. `DshInstaller.Shared.csproj` invokes `build.ps1` before building;
`pack-release.ps1` also compiles the template as an early check. No .NET 8, WinUI, browser,
external ZIP program or npm runtime is required to open the migration EXE.
The runtime requirement is the Windows .NET Framework 4 WPF/System.IO.Compression components
(Windows 10/11 provide these; .NET Framework 4.5+ is required for ZIP and async).

## Data Mapping

| Archive target | Restore target |
| --- | --- |
| `home/sessions`, `home/storages`, `home/attachments` | selected `DSH_HOME` / same directory |
| `home/skills` | selected `DSH_HOME/skills` |
| `agents/skills` | selected `DSH_AGENTS_HOME/skills` |
| `home/plugins` | selected `DSH_HOME/plugins` |
| `home/profiles/desktop` | selected `DSH_HOME/profiles/desktop` |
| `home/.credentials.yaml`, `home/.env` | selected `DSH_HOME`; sensitive, opt-in, existing files preserved |
| `compat/.dafeiyu/...` | selected `DSH_HOME/.dafeiyu-compat-import/.dafeiyu/...` |

Explicit source HOME is exact: it never falls back to another data source. Without an explicit HOME,
a portable launcher uses `<root>/.dsh`. An official desktop installation (identified by its CLI shim)
uses `DSH_HOME` or the current user's `.dsh`. Launcher settings are read only when no root was supplied.
Source `DSH_AGENTS_HOME/skills` (default current-user `.agents/skills`) is included in the skills group.
Official installation `resources/runtime/office-skills` and the entire immutable resource tree are excluded.
Dafeiyu metadata uses the same narrow portable allowlist as existing backup logic; official DSH does not
interpret these preferences. Credentials and profile configurations can contain secrets. No encryption
is claimed: hashes detect corruption and are not authentication against a maliciously repackaged EXE.

## Profiles And Plugins

The primary source is `web` for portable installs and `desktop` for official desktop installs, with an
explicit fallback to the other profile if the expected profile is absent. Other custom profiles are not
flattened into desktop. The mapping and limitation are reported in warnings.
Official installed ASAR evidence: `DesktopProjectManager` calls `createPluginProfile`, which uses
`PROFILE_TEMPLATES.web.bundles`: `@deepseek-ai/dsh-base` and `@deepseek-ai/dsh-web-app`.
The Electron application separately boots `profile: "desktop"` through `dsh-desktop-host`; substituting
`dsh-desktop-app` on the basis of its name would not reflect this installed version.

Profile package.json, cordis.yml, cordis.patch.yml, pnpm-workspace.yaml and compatibility.json are
portable. Existing configuration files are preserved, with a conflict entry in the restore result.
Plugin merge is explicit: missing third-party dependencies and enabled bundles are appended; existing
dependencies and official bundles keep their values. A replaced manifest has a sibling backup.
With merge disabled, the manifest follows the preserve-existing policy.

There is no blind node_modules copy. A local `file:`/`link:` dependency is portable only when its plain
source directory is within the selected HOME or the legacy root's `plugins`. Its source is exported to
`plugins/migration/<name>` and its spec becomes `file:../../plugins/migration/<name>`. Missing/external
local paths are removed from dependencies and enabled bundles and reported. Registry npm specs remain.
Official `dsh plugin --profile desktop install` must rebuild runtime dependencies; native modules,
plugin/DSH version approvals, and conflicting config can require manual work. The helper does not run
a shell or npm automatically and does not claim every plugin immediately works after extraction.

## Safety And Protocol

Format: PE stub, ZIP segment, 64-byte footer. Footer contains ASCII `DSH-OFFICIAL-SFX`, protocol 1,
little-endian ZIP length and SHA-256 of the entire ZIP. `manifest.json` lists exact paths, groups,
lengths and SHA-256 per file. The helper verifies the footer and every entry before creating staging
or writing target files, including case-insensitive duplicates, unlisted files, file/directory collisions,
reparse metadata, traversal, Windows reserved names, size/count/ratio limits, and destination allowlists.
Limits: 250,000 data files, 20 GiB, traversal depth 64, 1,000,000 source entries; compressed entries
must be within 1,000:1 except an allowance for entries under 1 MiB.

Source scan uses bounded per-directory enumeration, skips reparse children, and rechecks plain
ancestors before opening each file. Paths are rechecked at restore preflight and commit. These are
best-effort checks against concurrent filesystem mutation, not an atomic defense against a privileged
attacker racing directory replacements. Source files are opened read-only with read sharing.

Export staging is a unique sibling in the output directory and EXE commit is a no-replace move.
Restore staging is beside the archive; each new target uses its own same-directory partial then a
no-replace move. Existing target files are skipped; cancellation preserves earlier committed files.
The operation is not one global transaction. Completed outputs and existing credentials are retained.
Restores for one HOME use a mutex. Official running detection uses process image paths under the
selected official installation and QueryFullProcessImageName fallback; no force termination.

Install detection uses HKCU/HKLM uninstall entries and validates the executable and official CLI
layout. With multiple candidates no root is chosen automatically; the user supplies the target.
The helper runs as the current user without elevation and requires explicit close confirmation.

## Verification

`tools/OfficialDshExportRegression/run.ps1` builds the shared assembly and real helper, exports a real
EXE, executes its verify and restore modes, and tests exact HOME resolution, sensitive data,
profile-only plugins, shared skills, mappings, conflicts, source preservation, cancellation, junctions,
malicious ZIP paths/duplicates/unlisted files and SHA-256 corruption. Every fixture is isolated on G:.
`ui-smoke.ps1` launches the payload EXE without CLI switches, inspects accessible controls, resizes,
scrolls, captures screenshots, and closes normally. It waits for payload validation and checks that
groups, close confirmation and restore are visible in the default 800 by 780 DIP window. The three
browse buttons use Windows Shell folder bitmaps rather than private font glyphs; screenshot crops
and colored-pixel checks verify each icon. Compact windows retain vertical scrolling.
The regression suite includes 37 cases, including real EXE creation of a missing desktop profile
with the installed official default bundles and byte-for-byte manifest preservation with merge disabled.
`official-runtime-evidence.ps1` reads the selected official ASAR in place, records its SHA-256 and
the desktop path, host profile and default-bundle source lines in the G: evidence directory.
`official-cli-probe.ps1` accepts the official root
and creates an isolated data HOME before invoking the official plugin help command. It never installs
dependencies. The absent-plugin probe is expected to report the official rebuild instruction.
