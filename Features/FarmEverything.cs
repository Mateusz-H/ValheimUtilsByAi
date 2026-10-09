using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Wild berry bushes, mushrooms, thistle and dandelions can be uprooted with the Cultivator (the Remove button, middle
// mouse). Each drops a seed item: a clone of the plant's own produce with the food stats removed. The seed pays for a
// Cultivator piece cloned from the wild plant. It is placed bare and grows its first harvest after a configurable
// number of minutes (default 1.5x the wild respawn), then keeps regrowing at that pace.
internal static class FarmEverything
{
    private static readonly string[] WildPlants =
    {
        "RaspberryBush", "BlueberryBush", "CloudberryBush",
        "Pickable_Mushroom", "Pickable_Mushroom_yellow", "Pickable_Mushroom_blue",
        "Pickable_Thistle", "Pickable_Dandelion",
    };

    private sealed class Plant
    {
        public readonly GameObject Seed;
        public readonly Piece Piece;
        public readonly ConfigEntry<int> Minutes;

        public Plant(GameObject seed, Piece piece, ConfigEntry<int> minutes)
        {
            Seed = seed;
            Piece = piece;
            Minutes = minutes;
        }
    }

    // Keyed by prefab name, under both the wild plant and its planted clone.
    private static readonly Dictionary<string, Plant> Plants = new();
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigFile _config = null!;

    public static void Bind(ConfigFile config)
    {
        _config = config;
        _enabled = config.Bind(
            "World",
            "FarmEverything",
            true,
            "Uproot wild berry bushes, mushrooms, thistle and dandelions with the Cultivator (Remove button) to get " +
            "seeds, and plant them on cultivated ground with the Cultivator. Planted ones regrow slower than wild " +
            "ones; minutes per plant are set below. Off: no uprooting, pieces hidden; planted ones keep growing.");
        _enabled.SettingChanged += (_, _) =>
        {
            foreach (var plant in Plants.Values) plant.Piece.m_enabled = _enabled.Value;
        };

        PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
    }

    private static void RegisterPrefabs()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefabs;
        var saplingPlaceEffect = PrefabManager.Instance.GetPrefab("sapling_carrot")?.GetComponent<Piece>()?.m_placeEffect;

        foreach (var wildName in WildPlants)
        {
            var wild = PrefabManager.Instance.GetPrefab(wildName);
            var wildPickable = wild?.GetComponent<Pickable>();
            if (wild == null || wildPickable == null)
            {
                Plugin.Log.LogWarning($"FarmEverything: pickable prefab {wildName} not found, skipped");
                continue;
            }
            var produce = wildPickable.m_itemPrefab.GetComponent<ItemDrop>().m_itemData;
            var produceName = produce.m_shared.m_name;

            var seed = PrefabManager.Instance.CreateClonedPrefab("FarmEverything_Seed_" + wildName, wildPickable.m_itemPrefab);
            var shared = seed.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = produceName + " seeds";
            shared.m_description = "Plant on cultivated ground with the Cultivator. " +
                                   "Uproot a plant with the Cultivator (Remove) to get its seeds.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
            shared.m_food = shared.m_foodStamina = shared.m_foodRegen = shared.m_foodEitr = shared.m_foodBurnTime = 0f;
            shared.m_consumeStatusEffect = null;
            ItemManager.Instance.AddItem(new CustomItem(seed, false));

            var wildMinutes = wildPickable.m_respawnTimeMinutes;
            var minutes = _config.Bind(
                "World",
                $"FarmEverything{wildName}Minutes",
                wildMinutes > 0f ? Mathf.RoundToInt(wildMinutes * 1.5f) : 360,
                $"Minutes until a planted {wildName} grows its harvest, after planting and after each pick. " +
                $"Wild ones take {wildMinutes:0}.");

            var planted = PrefabManager.Instance.CreateClonedPrefab("FarmEverything_" + wildName, wild);
            var pickable = planted.GetComponent<Pickable>();
            pickable.m_defaultPicked = true;
            pickable.m_respawnTimeInitMin = pickable.m_respawnTimeInitMax = 0f;
            pickable.m_respawnTimeMinutes = minutes.Value;
            var piece = planted.AddComponent<Piece>();
            piece.m_name = produceName + " plant";
            piece.m_description = "Planted bare; the harvest grows after a while and regrows after every pick. " +
                                  "Uproot with the Cultivator (Remove) to get the seeds back.";
            piece.m_icon = produce.GetIcon();
            piece.m_groundOnly = piece.m_cultivatedGroundOnly = piece.m_clipGround = true;
            piece.m_randomTarget = false;
            if (saplingPlaceEffect != null) piece.m_placeEffect = saplingPlaceEffect;
            piece.m_resources = new[] { new Piece.Requirement { m_resItem = seed.GetComponent<ItemDrop>(), m_amount = 1 } };
            PieceManager.Instance.AddPiece(new CustomPiece(planted, false, new PieceConfig { PieceTable = PieceTables.Cultivator }));
            piece.m_enabled = _enabled.Value;

            var plant = new Plant(seed, piece, minutes);
            Plants[wildName] = plant;
            Plants[planted.name] = plant;
        }
    }

    // Same as Player.m_removeRayMask plus the item layer some pickables sit on. No character layers, so the ray
    // never stops on the player's own body.
    private static readonly int UprootMask =
        LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle", "item");

    // Hover is cleared while a build tool is out, so the plant under the crosshair is raycast here. The Remove release
    // is read exactly like vanilla; m_blockRemove stops vanilla from treating the same release as a piece removal.
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    private static class UprootWithCultivator
    {
        private static void Prefix(
            Player __instance, bool takeInput, ref bool ___m_blockRemove, ref float ___m_lastToolUseTime, ZSyncAnimation ___m_zanim)
        {
            if (!_enabled.Value || !takeInput || Plants.Count == 0 || !__instance.InPlaceMode() || __instance.IsDead()) return;
            if (__instance.GetBuildTool().name != PieceTables.Cultivator || Hud.IsPieceSelectionVisible()) return;
            if (ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltKeys")) return;
            var released = (!ZInput.IsGamepadActive() && ZInput.GetButtonUp("Remove")) || ZInput.GetButtonUp("JoyRemove");
            if (!released || Time.time - ___m_lastToolUseTime < __instance.m_removeDelay) return;

            var camera = GameCamera.instance.transform;
            if (!Physics.Raycast(camera.position, camera.forward, out var hit, 50f, UprootMask)) return;
            if (Vector3.Distance(hit.point, __instance.m_eye.position) > __instance.m_maxPlaceDistance) return;
            var pickable = hit.collider.GetComponentInParent<Pickable>();
            if (pickable == null || !Plants.TryGetValue(Utils.GetPrefabName(pickable.gameObject.name), out var plant)) return;
            var view = pickable.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return;

            ___m_blockRemove = true;
            ___m_lastToolUseTime = Time.time;
            var position = pickable.transform.position;
            view.ClaimOwnership();
            pickable.m_pickEffector.Create(position, Quaternion.identity);
            ItemDrop.OnCreateNew(Object.Instantiate(plant.Seed, position + Vector3.up * 0.5f, Quaternion.identity));
            ZNetScene.instance.Destroy(pickable.gameObject);
            __instance.FaceLookDirection();
            ___m_zanim.SetTrigger(__instance.RightItem.m_shared.m_attack.m_attackAnimation);
        }
    }

    // Runs on the owner once a minute per planted clone; writing the instance field keeps config edits live.
    [HarmonyPatch(typeof(Pickable), "UpdateRespawn")]
    private static class PlantedRegrowTime
    {
        private static void Prefix(Pickable __instance)
        {
            if (Plants.Count == 0) return;
            var name = Utils.GetPrefabName(__instance.gameObject.name);
            if (Plants.TryGetValue(name, out var plant) && name == plant.Piece.name)
                __instance.m_respawnTimeMinutes = plant.Minutes.Value;
        }
    }
}
