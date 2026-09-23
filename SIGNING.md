# SignPath code signing

Installer tag builds are signed with
[SignPath Foundation](https://signpath.org/), the free code signing service for
open source projects.

The certificate and publisher identity belong to SignPath Foundation. The
private key is managed by SignPath and is not available to this repository.

## One-time SignPath setup

1. Apply at <https://signpath.org/apply>.
2. Create a SignPath project for this repository.
3. Link the project to the predefined **GitHub.com** trusted build system.
4. Create an artifact configuration named `installer-inner` using
   [`.signpath/installer-inner-artifact-configuration.xml`](.signpath/installer-inner-artifact-configuration.xml).
5. Create an artifact configuration named `installer-setup` using
   [`.signpath/installer-setup-artifact-configuration.xml`](.signpath/installer-setup-artifact-configuration.xml).
6. Create a release signing policy, for example `release-signing`.
7. Create an API token for a user that can submit requests to this project.

Add these repository variables under **Settings > Secrets and variables >
Actions > Variables**:

| Variable | Value |
| --- | --- |
| `SIGNPATH_ORGANIZATION_ID` | SignPath organization ID |
| `SIGNPATH_PROJECT_SLUG` | Project slug created in SignPath |
| `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` or the chosen slug |
| `SIGNPATH_INNER_ARTIFACT_CONFIGURATION_SLUG` | `installer-inner` |
| `SIGNPATH_FINAL_ARTIFACT_CONFIGURATION_SLUG` | `installer-setup` |

Add this repository secret under **Settings > Secrets and variables > Actions >
Secrets**:

| Secret | Value |
| --- | --- |
| `SIGNPATH_API_TOKEN` | API token created in SignPath |

## Release behavior

Tag builds now fail before publishing when the SignPath configuration is
missing. The workflow:

1. Builds the installer payload and its first-party executables.
2. Uses SignPath to sign `DSH-Installer.exe`, `DSH-Installer.dll`,
   `DSH-Uninstall.exe`, and `DshInstaller.Shared.dll`.
3. Verifies the inner signatures.
4. Packages the signed payload behind the bootstrapper.
5. Uses SignPath again to sign the final `DSH-Installer-Setup.exe`.
6. Verifies the final signature and publishes the signed Setup EXE.

Manual `workflow_dispatch` runs still produce an unsigned installer artifact
for CI diagnostics, but tag releases require signing.
