# ValheimUtilsByAi

BepInEx 5 + HarmonyX mod for Valheim, published on Thunderstore as `ByAi-ValheimUtils`, labelled AI-generated.
Features may be client-side, server-side or both; each one says which in `thunderstore/README.md`.

## Workflows

- `/add-feature <description>`: implement a toggleable feature the standard way (research, patch, config, docs, smoke test, branch).
- `/deploy [patch|minor|major|X.Y.Z]`: bump version, finalize changelog, tag, push. CI publishes.

## Build and test

- `dotnet build -c Release` compiles and produces the Thunderstore zip in `dist/`.
  Target is `net462` because the Jötunn NuGet ships only a net462 lib; its build props are excluded
  (`ExcludeAssets="build"`) since they expect a Steam install and publicized assemblies.
- Game assemblies come from the free dedicated server, not Steam. Default path in the csproj is
  `C:/Users/mateu/valheim_server/valheim_server_Data/Managed`; override with `-p:ValheimManaged=...`.
  Refresh after a game patch: `steamcmd +force_install_dir <dir> +login anonymous +app_update 896660 +quit`.
- Smoke test without the game client: copy `bin/Release/ValheimUtilsByAi.dll` to the server's
  `BepInEx/plugins`, run `valheim_server.exe -nographics -batchmode -name t -port 2456 -world t -password tttttttt`,
  then check `BepInEx/LogOutput.log` for `Loading [ValheimUtilsByAi` and any Harmony errors.
  `AsyncResourceUpload failed` in that log is headless noise.
- No tuples (`(a, b)`, `var (x, y) = ...`): they compile against net462 but the game's Mono runtime has no
  `System.ValueTuple`, so the plugin fails to load with a `TypeLoadException`. Use a small class instead.
- Before writing a Harmony patch, decompile the target with `ilspycmd -t <Type> <path>/assembly_valheim.dll`
  and grep for every caller. Patch the shared method, not each caller.

## Feature rules

1. One file in `Features/`: a static class with `Bind(ConfigFile)` and its `[HarmonyPatch]` methods.
2. Call `Bind` from `Plugin.Awake`.
3. Toggles must work at runtime. Never mutate game state permanently based on config; restore it in a postfix
   or check the config value on every call.
4. Document the feature, its config key and its side (Client / Server / Both) in `thunderstore/README.md`,
   and add a bullet under `## Unreleased` in `thunderstore/CHANGELOG.md`.
5. Jötunn (`JotunnLib` NuGet, Thunderstore dep `ValheimModding-Jotunn`) is allowed when it saves real work:
   custom prefabs, items, pieces, recipes, localization, mod-side RPCs. Plain Harmony tweaks of vanilla
   behaviour stay Jötunn-free. Clone vanilla prefabs in `PrefabManager.OnVanillaPrefabsAvailable`; never
   touch `ObjectDB`/`ZNetScene` by hand. The smoke-test server needs `Jotunn.dll` in `BepInEx/plugins`.

## Releasing

`<Version>` in `ValheimUtilsByAi.csproj` is the single source for the DLL, plugin attribute and manifest.
Tag `vX.Y.Z` on `main` triggers `.github/workflows/release.yml`: it checks the tag matches the csproj and
changelog, downloads the dedicated server for assemblies, builds, creates a GitHub Release with the zip and
runs `tcli publish` using `thunderstore.toml` and the `THUNDERSTORE_API_TOKEN` repository secret.
