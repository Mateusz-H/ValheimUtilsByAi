# ValheimUtilsByAi (dev)

BepInEx 5 plugin for Valheim. See `thunderstore/README.md` for the user-facing description.

## Build

Needs the game's managed assemblies. Either a Steam install (`Valheim\valheim_Data\Managed`)
or the free dedicated server:

    steamcmd +force_install_dir C:\valheim_server +login anonymous +app_update 896660 +quit

Then:

    dotnet build -c Release -p:ValheimManaged="C:\valheim_server\valheim_server_Data\Managed"

`dist/ValheimUtilsByAi-<version>.zip` is the Thunderstore upload. Bump `<Version>` in the
csproj and add a `CHANGELOG.md` entry before publishing.
