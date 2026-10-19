# Thunderstore publishing

## Version and deployment policy

- Patch versions (`0.0.1`, `0.1.7`, `1.2.3`) are local builds: no automatic tag, GitHub Release, or Thunderstore upload.
- Stable minor/major versions ending in `.0` (`0.2.0`, `1.0.0`, `1.3.0`) deploy when their GitHub Release is published.
- Drafts, prereleases, non-version tags, and `0.0.0` never deploy. The existing public 0.1.6 predates this policy.

## Automated releases

GitHub Actions cannot build the native plugins from a clean hosted runner without the matching proprietary game interop files. Those files stay local and are not committed or uploaded. Release preparation therefore builds/tests on the development PC; hosted CI independently tests Core/Shared and validates the exact upload before publishing.

1. Set the version consistently in `Directory.Build.props`, `docs/versions.json`, `thunderstore/manifest.json`, all three `BepInPlugin` attributes, and `RegistrationPacket.ClientVersion`. Update the player guide and changelog too.
2. For a public version, commit and push to `origin/main`.
3. Run `./scripts/release.ps1 -Version 0.2.0` (using the intended version).
4. Review the draft release on [GitHub](https://github.com/lucas98sf/CombatMeter/releases), then publish it. That is the deployment trigger.

For a patch such as `./scripts/release.ps1 -Version 0.1.7`, the script only builds/tests/packages locally. A manually published patch GitHub Release is also skipped by CI.

The release script attaches the ZIP and a build record containing its version, Git commit, and SHA256. CI checks the tag against source/assembly versions, the build record, the exact permitted ZIP files, and byte-for-byte matching README/manifest/changelog/icon. It then uploads the already verified ZIP with the official TCLI pinned to 0.2.4. All GitHub actions are pinned to commit hashes. CI does not generate a replacement ZIP or silently rewrite versions.

Publish retries accept an already uploaded version only when its downloaded bytes match the release asset. Different bytes under an existing version fail; make a new version instead. Failed uploads can be rerun from GitHub Actions.

### One-time authentication

Create a **BiribaMods** service-account token in Thunderstore team settings, then save it as the repository Actions secret **THUNDERSTORE_TOKEN** at [GitHub settings](https://github.com/lucas98sf/CombatMeter/settings/secrets/actions). Do not commit the token or put it in a command-line argument. The workflow exposes it only to the publishing step as `TCLI_AUTH_TOKEN`; ordinary push/PR CI has no upload credentials.

`thunderstore.toml` selects BiribaMods, CombatMeter, V Rising, and the Client/Server/Mods/Oakveil categories. The ZIP's `thunderstore/README.md` remains the full Thunderstore page text; its `manifest.json` links to the [GitHub repository](https://github.com/lucas98sf/CombatMeter).

The public package contains both process-filtered plugins and Core, Shared, and Runtime. Networking is built into CombatMeter; Emberglass is not required. ServerLaunchFix is optional for dedicated servers and required for local hosting, so it is documented rather than forced as a dependency.

## Build

From the repository root:

```powershell
./scripts/build.ps1
./scripts/package-thunderstore.ps1
```

Output: `artifacts/CombatMeter-Thunderstore-0.1.7.zip` (the version comes from `docs/versions.json`). This is the Thunderstore upload artifact; the older separate Client/Server ZIPs are manual-install archives without Thunderstore metadata.

The package script checks matching manifest and assembly versions, the icon dimensions, and root metadata files. A fresh staging directory and explicit DLL list exclude local configurations, logs, game assemblies, SDKs, and hot-reload files. Build before packaging; the package script does not rebuild binaries.

Public metadata, README, changelog, and icon are in `thunderstore/`. `scripts/create-package-icon.ps1` regenerates the original package icon without game assets.

## Upload

1. Sign in to [Thunderstore's uploader](https://thunderstore.io/package/create/).
2. Choose the package ZIP, your publishing team, and the **V Rising** community.
3. Choose applicable **Mods**, **Client**, **Server**, and current game-update categories.
4. Submit and verify the published package page and version.

The first public build is marked early access. The public README identifies the installed game version and remaining multiplayer/contest validation; see `docs/VALIDATION.md` for the detailed record. The package website links to the public GitHub repository.

For subsequent releases, increase the package and plugin versions together, update the changelog, build/test, and upload under the **same team and package name**. Changes to permanent Runtime/Core/Shared assemblies require a coordinated client/server restart.
