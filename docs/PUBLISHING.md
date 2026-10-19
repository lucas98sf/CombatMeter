# Thunderstore publishing

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

The first public build is marked early access. The public README identifies the installed game version and remaining multiplayer/contest validation; see `docs/VALIDATION.md` for the detailed record. There is no configured public source repository yet, so `website_url` is empty rather than pointing to an unrelated project.

For subsequent releases, increase the package and plugin versions together, update the changelog, build/test, and upload under the **same team and package name**. Changes to permanent Runtime/Core/Shared assemblies require a coordinated client/server restart.
