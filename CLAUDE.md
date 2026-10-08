# ValheimUtilsByAi

BepInEx 5 + HarmonyX mod for Valheim, published on Thunderstore, labelled AI-generated. Client-side only.

## Build and test

- `dotnet build -c Release` compiles and produces the Thunderstore zip in `dist/`.
- Game assemblies come from the free dedicated server, not Steam. Default path in the csproj is
  `C:\Users\mateu\valheim_server\valheim_server_Data\Managed`; override with `-p:ValheimManaged=...`.
  Refresh after a game patch: `steamcmd +force_install_dir <dir> +login anonymous +app_update 896660 +quit`.
- Smoke test without the game client: copy `bin/Release/ValheimUtilsByAi.dll` to the server's
  `BepInEx/plugins`, run `valheim_server.exe -nographics -batchmode -name t -port 2456 -world t -password tttttttt`,
  then check `BepInEx/LogOutput.log` for `Loading [ValheimUtilsByAi` and any Harmony errors.
  `AsyncResourceUpload failed` in that log is headless noise.
- Before writing a Harmony patch, decompile the target with `ilspycmd -t <Type> <path>/assembly_valheim.dll`
  and grep for every caller. Patch the shared method, not each caller.

## Adding a feature

1. One file in `Features/`: a static class with `Bind(ConfigFile)` and its `[HarmonyPatch]` methods.
2. Call `Bind` from `Plugin.Awake`.
3. Toggles must work at runtime. Never mutate game state permanently based on config; restore it in a postfix
   or check the config value on every call.
4. Document the feature and its config key in `thunderstore/README.md`.

## Releasing

Bump `<Version>` in `ValheimUtilsByAi.csproj` (single source for the DLL, plugin attribute and manifest),
add a `thunderstore/CHANGELOG.md` entry, build, upload `dist/ValheimUtilsByAi-<version>.zip`.
Thunderstore dependency string for BepInEx lives in `thunderstore/manifest.json`.
