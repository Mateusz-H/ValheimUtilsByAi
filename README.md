# ValheimUtilsByAi (dev)

BepInEx 5 plugin for Valheim, published as [ByAi-ValheimUtils](https://thunderstore.io/package/ByAi/ValheimUtils/).
See `thunderstore/README.md` for the user-facing description and `CLAUDE.md` for conventions.

## Build

Needs the game's managed assemblies. Either a Steam install (`Valheim\valheim_Data\Managed`)
or the free dedicated server:

    steamcmd +force_install_dir C:\valheim_server +login anonymous +app_update 896660 +quit

Then:

    dotnet build -c Release -p:ValheimManaged="C:\valheim_server\valheim_server_Data\Managed"

`dist/ValheimUtilsByAi-<version>.zip` is the Thunderstore package.

## Release

Push a tag `vX.Y.Z` on `main` whose version matches `<Version>` in the csproj and a `## X.Y.Z` section in
`thunderstore/CHANGELOG.md`. GitHub Actions builds, creates the GitHub Release and publishes to Thunderstore.
Requires the `THUNDERSTORE_API_TOKEN` repository secret (Thunderstore team ByAi, service account token).
