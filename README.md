# Frontier Map Better Prospecting Compatibility

Fork of [Olendril's compatibility mod](https://github.com/olendril/vs-frontiermapbetterprospecting), updated and checked against **Frontier's Map 2.0.1**, **BetterEr Prospecting 3.4.12**, and **Vintage Story 1.22.7** (.NET 10).

Completed density readings go through Frontier's existing network, naming dialog, ledger, and map-pin workflow. The integration forwards the final BetterEr reading instead of generating a second vanilla reading. Quality labels use BetterEr's multiplier, and densities retain their original precision. Frontier's Map 2.0.1 already displays two decimal places in the ledger and pin tooltips; no renderer patch is needed.

Stone-search readings are excluded. Node, proximity, and borehole modes keep their existing behavior. An empty density reading still opens Frontier's normal naming dialog. The original Frontier prospecting postfix is removed while the bridge is active to avoid duplicate dialogs, and restored when the compatibility mod is disposed.

## Install

Install `artifacts/package/frontiermapbetterprospecting.zip` alongside Frontier's Map 2.0.1 and BetterEr Prospecting 3.4.12 on both the client and server. Replace any previous `frontiermapbetterprospecting` ZIP; the mod ID is retained from upstream.

These versions are minimum requirements in the manifest. Later versions may need another compatibility check.

## Build and package

1. Install the .NET 10 SDK.
2. Extract `ItemFrontiersMap.dll` from Frontier's Map 2.0.1 into `references/`.
3. Extract `BetterErProspecting.dll` from BetterEr Prospecting 3.4.12 into `references/`.
4. Set `VINTAGE_STORY_PATH` (or `VINTAGE_STORY`) to your Vintage Story installation.

```sh
export VINTAGE_STORY_PATH=/path/to/vintagestory
dotnet build bettererprospectingfrontiermapcompat.sln -c Release
bash build/package.sh
```

Alternatively, set `VintageStoryPath` in the ignored `Directory.Build.props.local` file. Override DLL locations with MSBuild properties `FrontiersMapPath` and `BetterErProspectingPath`. Reference DLLs are compile-time inputs and are excluded from the mod ZIP.

## Validation

```sh
dotnet run --project tests/CompatibilityChecks.csproj -c Release
```

The checks use the actual game and mod assemblies to validate packet precision, BetterEr quality boundaries, stone-mode exclusion, Harmony patch installation and forwarding, and patch cleanup. An in-game check is still needed for the complete GUI workflow: take a density reading, name it once, add it from the ledger, and verify the map tooltip. Repeat with vanilla/one-shot density enabled and on a dedicated server, then confirm stone and node modes do not open an ore-reading dialog.

Upstream source: commit `eb8279a` on `olendril/vs-frontiermapbetterprospecting/main`. Original author credit is retained. Upstream does not include a license file.
