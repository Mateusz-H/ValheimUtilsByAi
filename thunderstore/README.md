# ValheimUtils

> **AI generated.** This mod is written by an AI (Claude) under human direction and testing.

Small quality-of-life tweaks for Valheim. Every feature has its own switch in
`BepInEx/config/mh.ValheimUtilsByAi.cfg` (generated on first launch). Client-side only,
works on vanilla servers.

## Features

### Craft without roof (`[Crafting] CraftWithoutRoof`, default: on)

The vanilla game refuses to open a workbench, forge or any other crafting station that is
not under a roof or is "too exposed". This removes that check. Everything else is untouched:
a forge still needs fire, stations still need their extensions in range, build pieces still
need a station nearby.

## Install

Drop `ValheimUtilsByAi.dll` into `BepInEx/plugins` or install with a mod manager.
