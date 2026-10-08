using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Custom "FeedingTrough" chest. Hungry tamed animals walk to the nearest trough holding food they eat and take
// one item from it, exactly as if it lay on the ground.
//
// Performance: nothing runs per frame for an animal without a trough target beyond one dictionary count check.
// Troughs are only searched when the vanilla food search fires (every m_consumeSearchInterval, 10 s by default),
// only for tamed hungry animals, and only when no food lies on the ground nearby. The search walks a static list
// of loaded troughs (no physics query) and runs one HavePath for the chosen trough.
internal static class FeedingTrough
{
    private const string TroughPrefab = "FeedingTrough";
    private const string TroughToken = "$piece_feedingtrough";

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _range = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "FeedingTrough",
            true,
            "Adds the Feeding Trough chest. Hungry tamed animals walk to a nearby trough and eat food they like " +
            "from it. The trough stays craftable when this is off; animals just stop using it.");
        _range = config.Bind(
            "World",
            "FeedingTroughRange",
            15f,
            "How far in meters a hungry tamed animal looks for a Feeding Trough.");

        PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
    }

    private static void RegisterPrefabs()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterPrefabs;

        var config = new PieceConfig
        {
            Name = TroughToken,
            Description = "$piece_feedingtrough_description",
            PieceTable = PieceTables.Hammer,
            Category = PieceCategories.Misc,
            CraftingStation = CraftingStations.Workbench,
        };
        config.AddRequirement("Wood", 10, true);
        config.AddRequirement("Resin", 2, true);
        var trough = new CustomPiece(TroughPrefab, "piece_chest_wood", config);
        trough.PiecePrefab.GetComponent<Container>().m_name = TroughToken;
        PieceManager.Instance.AddPiece(trough);

        var localization = LocalizationManager.Instance.GetLocalization();
        localization.AddTranslation(TroughToken.TrimStart('$'), "Feeding Trough");
        localization.AddTranslation("piece_feedingtrough_description",
            "Hungry tamed animals nearby eat food they like from this chest.");
    }

    // ---------------------------------------------------------------- trough registry

    private static readonly List<Container> Troughs = new();

    [HarmonyPatch(typeof(Container), "Awake")]
    private static class RegisterTrough
    {
        private static void Postfix(Container __instance)
        {
            if (__instance.name.StartsWith(TroughPrefab, StringComparison.Ordinal)) Troughs.Add(__instance);
        }
    }

    private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    private static readonly Func<Container, bool> ContainerLoad =
        AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));

    private static bool Usable(Container trough)
    {
        var view = ContainerView(trough);
        return view != null && view.IsValid() && !trough.IsInUse() && view.GetZDO().GetInt(ZDOVars.s_inUse) == 0;
    }

    // First item in the trough the animal eats, with the matching prefab passed to m_onConsumedItem.
    private static ItemDrop.ItemData? FindFood(Container trough, List<ItemDrop> diet, out ItemDrop? prefab)
    {
        foreach (var item in trough.GetInventory().GetAllItems())
        {
            foreach (var food in diet)
            {
                if (food.m_itemData.m_shared.m_name != item.m_shared.m_name) continue;
                prefab = food;
                return item;
            }
        }
        prefab = null;
        return null;
    }

    // ---------------------------------------------------------------- animal side

    private static readonly AccessTools.FieldRef<BaseAI, Tameable> AiTameable =
        AccessTools.FieldRefAccess<BaseAI, Tameable>("m_tamable");
    private static readonly AccessTools.FieldRef<BaseAI, Character> AiCharacter =
        AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
    private static readonly AccessTools.FieldRef<BaseAI, ZSyncAnimation> AiAnimator =
        AccessTools.FieldRefAccess<BaseAI, ZSyncAnimation>("m_animator");
    private static readonly Func<BaseAI, float, Vector3, float, bool, bool> AiMoveTo =
        AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(AccessTools.Method(typeof(BaseAI), "MoveTo"));
    private static readonly Action<BaseAI, Vector3> AiLookAt =
        AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookAt"));
    private static readonly Func<BaseAI, Vector3, bool> AiHavePath =
        AccessTools.MethodDelegate<Func<BaseAI, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "HavePath"));

    // Animals currently walking to a trough. Empty almost always, so the per-frame prefix is a count check.
    private static readonly Dictionary<MonsterAI, Container> Targets = new();

    private static bool WantsFood(MonsterAI ai)
    {
        var tameable = AiTameable(ai);
        var character = AiCharacter(ai);
        return tameable != null && character != null && character.IsTamed() && tameable.IsHungry();
    }

    // Vanilla food search came back empty: look for a trough instead. Runs once per search interval per animal.
    [HarmonyPatch(typeof(MonsterAI), "FindClosestConsumableItem")]
    private static class FindTrough
    {
        private static void Postfix(MonsterAI __instance, ItemDrop __result)
        {
            Targets.Remove(__instance);
            if (!_enabled.Value || __result != null || Troughs.Count == 0 || !WantsFood(__instance)) return;

            var position = __instance.transform.position;
            var maxSqr = _range.Value * _range.Value;
            Container? best = null;
            var bestSqr = float.MaxValue;
            for (var i = Troughs.Count - 1; i >= 0; i--)
            {
                var trough = Troughs[i];
                if (trough == null) { Troughs.RemoveAt(i); continue; }
                var sqr = (trough.transform.position - position).sqrMagnitude;
                if (sqr > maxSqr || sqr >= bestSqr || !Usable(trough)) continue;
                if (FindFood(trough, __instance.m_consumeItems, out _) == null) continue;
                best = trough;
                bestSqr = sqr;
            }
            if (best == null || !AiHavePath(__instance, best.transform.position)) return;

            // Drop entries of animals unloaded mid-walk; only happens when a new target is set, which is rare.
            foreach (var stale in Targets.Keys.Where(ai => ai == null).ToList()) Targets.Remove(stale);
            Targets[__instance] = best;
        }
    }

    // Walk to the trough and eat, mirroring the vanilla ground-item branch of UpdateConsumeItem.
    [HarmonyPatch(typeof(MonsterAI), "UpdateConsumeItem")]
    private static class EatFromTrough
    {
        private static bool Prefix(MonsterAI __instance, Humanoid humanoid, float dt, ref bool __result)
        {
            if (Targets.Count == 0 || !Targets.TryGetValue(__instance, out var trough)) return true;
            if (!_enabled.Value || trough == null || !Usable(trough) || !WantsFood(__instance))
            {
                Targets.Remove(__instance);
                return true;
            }

            __result = true;
            var target = trough.transform.position;
            if (!AiMoveTo(__instance, dt, target, __instance.m_consumeRange + 1f, false)) return false;
            AiLookAt(__instance, target);
            if (!__instance.IsLookingAt(target, 20f)) return false;

            Targets.Remove(__instance);
            var view = ContainerView(trough);
            if (!view.IsOwner()) view.ClaimOwnership();
            ContainerLoad(trough);
            var food = FindFood(trough, __instance.m_consumeItems, out var prefab);
            if (food == null || !trough.GetInventory().RemoveOneItem(food)) return false;

            __instance.m_onConsumedItem?.Invoke(prefab!);
            humanoid.m_consumeItemEffects.Create(__instance.transform.position, Quaternion.identity);
            AiAnimator(__instance).SetTrigger("consume");
            return false;
        }
    }
}
