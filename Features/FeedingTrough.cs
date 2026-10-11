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
// of loaded troughs (no physics query) and runs one path query for the nearest trough with food.
//
// Only the trough's ZDO owner may edit it, because only the owner's edits are saved (Container.OnContainerChanged).
// An animal simulated by another game waits at the trough and asks the owner to hand it over, which the owner does
// only while nobody has it open, the same rule as vanilla Container.RPC_RequestOpen.
internal static class FeedingTrough
{
    internal const string TroughPrefab = "FeedingTrough";
    private const string TroughToken = "$piece_feedingtrough";
    private const string RequestOwnRpc = "mh_FeedingTrough_RequestOwn";
    private const float GiveUpSeconds = 30f;

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<float> _range = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "FeedingTrough",
            true,
            Plugin.Synced(
                "Adds the Feeding Trough chest. Hungry tamed animals walk to a nearby trough and eat food they like " +
                "from it. The trough stays craftable when this is off; animals just stop using it."));
        _range = config.Bind(
            "World",
            "FeedingTroughRange",
            15f,
            Plugin.Synced(
                "How far in meters a hungry tamed animal looks for a Feeding Trough.",
                new AcceptableValueRange<float>(1f, 50f)));

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

    // ---------------------------------------------------------------- trough registry and ownership hand-over

    private static readonly List<Container> Troughs = new();

    // Placement ghosts have no inventory. The RPC is registered regardless of the config: handing over a closed
    // trough is what vanilla does when another player opens it.
    [HarmonyPatch(typeof(Container), "Awake")]
    private static class RegisterTrough
    {
        private static void Postfix(Container __instance)
        {
            if (!__instance.name.StartsWith(TroughPrefab, StringComparison.Ordinal) || __instance.GetInventory() == null) return;
            var trough = __instance;
            ContainerView(trough).Register(RequestOwnRpc, sender => RPC_RequestOwn(trough, sender));
            Troughs.Add(trough);
        }
    }

    private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    private static readonly Func<Container, bool> ContainerLoad =
        AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));

    // Runs on the trough's owner. Only the owner hands over, so two games never edit the trough at once.
    private static void RPC_RequestOwn(Container trough, long sender)
    {
        var view = ContainerView(trough);
        if (!view.IsValid() || !view.IsOwner() || trough.IsInUse()) return;
        ZDOMan.instance.ForceSendZDO(sender, view.GetZDO().m_uid);
        view.GetZDO().SetOwner(sender);
    }

    private static readonly Dictionary<Container, float> NextRequest = new();

    private static void RequestOwnership(Container trough, ZNetView view)
    {
        var owner = view.GetZDO().GetOwner();
        // No owner: the server assigns one within a few seconds.
        if (owner == 0 || (NextRequest.TryGetValue(trough, out var next) && Time.time < next)) return;
        NextRequest[trough] = Time.time + 5f;
        view.InvokeRPC(owner, RequestOwnRpc);
    }

    // Not the s_inUse ZDO flag: only the owner rewrites it, so after its user's game crashed with the trough open it
    // stays set until someone opens and closes the trough. The owner refuses to hand over a trough that is open.
    private static bool Usable(Container trough)
    {
        var view = ContainerView(trough);
        return view != null && view.IsValid() && !trough.IsInUse();
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

    // Not a value tuple: the game's runtime has no System.ValueTuple assembly for a net462 build to bind to.
    private struct TroughWalk
    {
        public Container Trough;
        public float GiveUpAt;
    }

    // Animals walking to or waiting at a trough. Empty almost always, so the per-frame prefix is a count check.
    private static readonly Dictionary<MonsterAI, TroughWalk> Targets = new();

    // Summons are tamed and hungry from the start but only temporary, so they leave the feed to the livestock.
    private static bool WantsFood(MonsterAI ai)
    {
        var tameable = AiTameable(ai);
        var character = AiCharacter(ai);
        return tameable != null && character != null && character.IsTamed() && tameable.m_unsummonDistance <= 0f &&
               tameable.IsHungry();
    }

    // One meter more than the vanilla reach for ground food: the food lies in the trough, not at its centre.
    private static float EatRange(MonsterAI ai) => ai.m_consumeRange + 1f;

    private static readonly List<Vector3> PathCorners = new();

    // The trough cuts the navmesh around itself by the animal's size, so for a Lox or Moose walkable ground starts
    // farther from its centre than the 1 m vanilla HavePath allows. Check the path the walk itself takes instead
    // (MoveTo snaps its target to the navmesh within a wider radius), complete and ending within eating range.
    private static bool Reachable(MonsterAI ai, Vector3 troughPosition) =>
        Pathfinding.instance.GetPath(ai.transform.position, troughPosition, PathCorners, ai.m_pathAgentType, requireFullPath: true, cleanup: false) &&
        PathCorners.Count > 0 && Utils.DistanceXZ(PathCorners[PathCorners.Count - 1], troughPosition) < EatRange(ai);

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
                if (trough == null) { Troughs.RemoveAt(i); NextRequest.Remove(trough!); continue; }
                var sqr = (trough.transform.position - position).sqrMagnitude;
                if (sqr > maxSqr || sqr >= bestSqr || !Usable(trough)) continue;
                if (FindFood(trough, __instance.m_consumeItems, out _) == null) continue;
                best = trough;
                bestSqr = sqr;
            }
            if (best == null || !Reachable(__instance, best.transform.position)) return;

            // Drop entries of animals unloaded mid-walk; only happens when a new target is set, which is rare.
            foreach (var stale in Targets.Keys.Where(ai => ai == null).ToList()) Targets.Remove(stale);
            Targets[__instance] = new TroughWalk { Trough = best, GiveUpAt = Time.time + GiveUpSeconds };
        }
    }

    // Walk to the trough and eat, mirroring the vanilla ground-item branch of UpdateConsumeItem.
    [HarmonyPatch(typeof(MonsterAI), "UpdateConsumeItem")]
    private static class EatFromTrough
    {
        private static bool Prefix(MonsterAI __instance, Humanoid humanoid, float dt, ref bool __result)
        {
            if (Targets.Count == 0 || !Targets.TryGetValue(__instance, out var walk)) return true;
            var trough = walk.Trough;
            if (!_enabled.Value || trough == null || !Usable(trough) || !WantsFood(__instance) || Time.time > walk.GiveUpAt)
            {
                Targets.Remove(__instance);
                return true;
            }

            __result = true;
            var target = trough.transform.position;
            var range = EatRange(__instance);
            if (!AiMoveTo(__instance, dt, target, range, false)) return false;
            // MoveTo also reports arrival when it found no path or its path ended short, e.g. at a gate closed mid-walk.
            if (Utils.DistanceXZ(target, __instance.transform.position) >= range)
            {
                Targets.Remove(__instance);
                return false;
            }
            AiLookAt(__instance, target);
            if (!__instance.IsLookingAt(target, 20f)) return false;

            var view = ContainerView(trough);
            if (!view.IsOwner())
            {
                RequestOwnership(trough, view);
                return false;
            }

            Targets.Remove(__instance);
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
