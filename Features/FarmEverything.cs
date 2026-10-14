using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Wild berry bushes, mushrooms, thistle, dandelions and fiddlehead ferns can be uprooted with the Cultivator (the Remove button, middle
// mouse). Each drops a seed item: a clone of the plant's own produce with the food stats removed. The seed pays for a
// Cultivator piece cloned from the wild plant. It is placed bare and grows its first harvest after a configurable
// number of minutes (default 1.5x the wild respawn), then keeps regrowing at that pace. Vanilla saplings (crops,
// mushrooms, trees, vines) get a minutes setting too, which scales their grow time.
internal static class FarmEverything
{
    private static readonly string[] WildPlants =
    {
        "RaspberryBush", "BlueberryBush", "CloudberryBush", "LingonberryBush",
        "Pickable_Mushroom", "Pickable_Mushroom_yellow", "Pickable_Mushroom_blue", "Pickable_SmokePuff",
        "Pickable_Thistle", "Pickable_Dandelion", "Pickable_Fiddlehead",
    };

    private sealed class Plant
    {
        public readonly GameObject Seed;
        public readonly Piece Piece;
        public readonly ConfigEntry<int> Minutes;
        // Prefab child holding the whole model, shown shrunk while the harvest regrows. Null for berry bushes, which
        // only hide their berries.
        public readonly Transform? ModelShrunkWhileBare;

        public Plant(GameObject seed, Piece piece, ConfigEntry<int> minutes, Transform? modelShrunkWhileBare)
        {
            Seed = seed;
            Piece = piece;
            Minutes = minutes;
            ModelShrunkWhileBare = modelShrunkWhileBare;
        }
    }

    private const float BareModelScale = 0.4f;

    private static readonly System.Action<Player> UpdateKnownRecipesList =
        AccessTools.MethodDelegate<System.Action<Player>>(AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"));
    private static readonly System.Action<Player> UpdateAvailablePiecesList =
        AccessTools.MethodDelegate<System.Action<Player>>(AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList"));
    private static readonly System.Func<Player, float> GetBuildStamina =
        AccessTools.MethodDelegate<System.Func<Player, float>>(AccessTools.Method(typeof(Player), "GetBuildStamina"));
    private static readonly System.Func<Player, ItemDrop.ItemData, float> GetPlaceDurability =
        AccessTools.MethodDelegate<System.Func<Player, ItemDrop.ItemData, float>>(
            AccessTools.Method(typeof(Player), "GetPlaceDurability"));

    // Keyed by prefab name, under both the wild plant and its planted clone.
    private static readonly Dictionary<string, Plant> Plants = new();
    // Keyed by sapling prefab name.
    private static readonly Dictionary<string, ConfigEntry<int>> SaplingMinutes = new();
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigFile _config = null!;

    public static void Bind(ConfigFile config)
    {
        _config = config;
        _enabled = config.Bind(
            "World",
            "FarmEverything",
            true,
            Plugin.Synced(
                "Uproot wild berry bushes, mushrooms, thistle, dandelions and fiddlehead with the Cultivator (Remove " +
                "button) to get seeds, and plant them on cultivated ground with the Cultivator. Planted ones regrow " +
                "slower than wild ones; minutes per plant are set below, as are the grow times of vanilla saplings. " +
                "Off: no uprooting, pieces hidden, saplings grow at vanilla speed; planted ones keep growing."));
        _enabled.SettingChanged += (_, _) =>
        {
            foreach (var plant in Plants.Values) plant.Piece.m_enabled = _enabled.Value;
            // The Cultivator only re-reads m_enabled when its piece list refreshes, so refresh it now.
            if (Player.m_localPlayer == null) return;
            UpdateKnownRecipesList(Player.m_localPlayer);
            UpdateAvailablePiecesList(Player.m_localPlayer);
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
                Plugin.Synced(
                    $"Minutes until a planted {wildName} grows its harvest, after planting and after each pick. " +
                    $"Wild ones take {wildMinutes:0}.",
                    // At 0 vanilla never schedules the regrowth, and destroys a picked plant that hides nothing.
                    new AcceptableValueRange<int>(1, 10000)));

            var planted = PrefabManager.Instance.CreateClonedPrefab("FarmEverything_" + wildName, wild);
            var pickable = planted.GetComponent<Pickable>();
            pickable.m_defaultPicked = true;
            pickable.m_respawnTimeInitMin = pickable.m_respawnTimeInitMax = 0f;
            pickable.m_respawnTimeMinutes = minutes.Value;
            // Mushrooms, thistle, dandelion and fiddlehead hide their whole model, colliders included, while picked, so a
            // bare planted one would be invisible and impossible to hover or uproot. Their model shrinks instead.
            var hidden = pickable.m_hideWhenPicked;
            Transform? modelShrunkWhileBare = null;
            if (hidden != null && hidden.transform.parent == planted.transform &&
                planted.GetComponentsInChildren<Collider>(true).All(collider => collider.transform.IsChildOf(hidden.transform)))
            {
                modelShrunkWhileBare = hidden.transform;
                pickable.m_hideWhenPicked = null;
            }
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

            var plant = new Plant(seed, piece, minutes, modelShrunkWhileBare);
            Plants[wildName] = plant;
            Plants[planted.name] = plant;
        }

        // Every sapling prefab loaded by now: crops, Mistlands mushrooms, trees and vines.
        foreach (var sapling in PrefabManager.Cache.GetPrefabs(typeof(global::Plant)).Values.OfType<global::Plant>()
                     .OrderBy(sapling => sapling.name))
        {
            if (sapling.m_growTime <= 0f || SaplingMinutes.ContainsKey(sapling.name)) continue;
            // 0 rather than the vanilla minutes as default, so a game patch changing them is never overridden.
            SaplingMinutes[sapling.name] = _config.Bind(
                "World",
                $"FarmEverything{sapling.name}Minutes",
                0,
                Plugin.Synced(
                    $"Minutes until a planted {sapling.name} grows, at the earliest; 0 keeps vanilla, " +
                    $"{sapling.m_growTime / 60f:0} to {sapling.m_growTimeMax / 60f:0}. Each plant picks its own time " +
                    "in that range, which scales with this value.",
                    new AcceptableValueRange<int>(0, 10000)));
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
            Player __instance, bool takeInput, ref bool ___m_blockRemove, ref float ___m_lastToolUseTime,
            ref int ___m_buildRemoveDebt, ZSyncAnimation ___m_zanim)
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
            var cultivator = __instance.RightItem;
            if (!__instance.HaveStamina(cultivator.m_shared.m_attack.m_attackStamina))
            {
                Hud.instance.StaminaBarEmptyFlash();
                return;
            }

            ___m_blockRemove = true;
            ___m_lastToolUseTime = Time.time;
            var position = pickable.transform.position;
            view.ClaimOwnership();
            // As the owner, the pick runs right here, so the harvest drops before the plant is destroyed.
            if (pickable.GetPicked()) pickable.m_pickEffector.Create(position, Quaternion.identity);
            else pickable.Interact(__instance, false, false);
            ItemDrop.OnCreateNew(Object.Instantiate(plant.Seed, position + Vector3.up * 0.5f, Quaternion.identity));
            ZNetScene.instance.Destroy(pickable.gameObject);

            // Same cost as removing a piece with the hammer (Player.UpdatePlacement). The remove debt keeps
            // plant-and-uproot loops from raising Farming, which every planting does.
            __instance.AddNoise(50f);
            __instance.UseStamina(GetBuildStamina(__instance));
            if (__instance.GetBuildTool().m_skill != Skills.SkillType.None && ___m_buildRemoveDebt < 20) ___m_buildRemoveDebt++;
            if (cultivator.m_shared.m_useDurability)
                cultivator.m_durability -= GetPlaceDurability(__instance, cultivator) * Game.m_durabilityRate;
            __instance.FaceLookDirection();
            ___m_zanim.SetTrigger(cultivator.m_shared.m_attack.m_attackAnimation);
        }
    }

    // Removing a planted one with the Hammer (or vanilla Remove with the Cultivator) refunds its seed through
    // DropResources right after Player.RemovePiece claims it, then destroys it with its harvest. A grown one is picked
    // first, as when uprooting; as the owner, the pick runs right here. Not gated on the config: it only keeps the
    // harvest of plants that already exist.
    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    private static class HarvestOnRemove
    {
        private static void Prefix(Piece __instance)
        {
            var player = Player.m_localPlayer;
            if (Plants.Count == 0 || player == null) return;
            var pickable = __instance.GetComponent<Pickable>();
            if (pickable == null || pickable.GetPicked() || !Plants.ContainsKey(Utils.GetPrefabName(pickable.gameObject.name))) return;
            var view = pickable.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner()) pickable.Interact(player, false, false);
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

    // Vanilla saplings: the owner grows one once its seeded random time between m_growTime and m_growTimeMax has passed
    // (Plant.SUpdate, every 10 s), and PlantGrowthTimer shows the same value. Scaling the whole range keeps each plant's
    // own place in it, and plants already in the ground follow a changed setting.
    [HarmonyPatch(typeof(global::Plant), "GetGrowTime")]
    private static class SaplingGrowTime
    {
        private static void Postfix(global::Plant __instance, ref float __result)
        {
            if (!_enabled.Value || __instance.m_growTime <= 0f) return;
            if (!SaplingMinutes.TryGetValue(Utils.GetPrefabName(__instance.gameObject.name), out var minutes)) return;
            if (minutes.Value > 0) __result *= minutes.Value * 60f / __instance.m_growTime;
        }
    }

    // Awake covers loading, SetPicked every pick and regrow; both run on every client.
    [HarmonyPatch(typeof(Pickable), "Awake")]
    private static class ScaleBareModelOnLoad
    {
        private static void Postfix(Pickable __instance, bool ___m_picked) => ScaleBareModel(__instance, ___m_picked);
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.SetPicked))]
    private static class ScaleBareModelOnPick
    {
        private static void Postfix(Pickable __instance, bool picked) => ScaleBareModel(__instance, picked);
    }

    private static void ScaleBareModel(Pickable pickable, bool picked)
    {
        var name = Utils.GetPrefabName(pickable.gameObject.name);
        if (!Plants.TryGetValue(name, out var plant) || plant.ModelShrunkWhileBare == null || name != plant.Piece.name) return;
        var model = pickable.transform.Find(plant.ModelShrunkWhileBare.name);
        if (model != null) model.localScale = plant.ModelShrunkWhileBare.localScale * (picked ? BareModelScale : 1f);
    }
}
