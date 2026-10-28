using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// A sap extractor (SapCollector), on its ZDO owner, adds one sap per m_secPerUnit (60 s) up to m_maxLevel (10) and
// drains as much from the ancient root (ResourceRoot) under it; the root regrows m_regenPerSec (one sap per 400 s) up
// to its own m_maxLevel (50). Nothing ticks while nobody is near: on the first owner tick after the area loads, each
// catches up on the time since its saved lastTime, the extractor usually before the root, so it sees the root as it
// was left (drained) and throws its own waiting time away; the root's regrowth then trickles out at one a minute.
// Here the extractor's maximum is raised for the calls that read it (the root's stays vanilla), the root catches up
// first, the extractor's clock waits while it has no root to draw from, and the root loses only what fit in the
// extractor: vanilla drains the whole catch-up even when the extractor clamps it, and nothing at all when it asks for
// exactly the root's level.
internal static class SapCapacity
{
    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<int> _amount = null!;

    private static readonly AccessTools.FieldRef<SapCollector, ZNetView> CollectorView =
        AccessTools.FieldRefAccess<SapCollector, ZNetView>("m_nview");
    private static readonly AccessTools.FieldRef<SapCollector, ResourceRoot> CollectorRoot =
        AccessTools.FieldRefAccess<SapCollector, ResourceRoot>("m_root");
    private static readonly AccessTools.FieldRef<ResourceRoot, ZNetView> RootView =
        AccessTools.FieldRefAccess<ResourceRoot, ZNetView>("m_nview");
    private static readonly Action<ResourceRoot> RootUpdateTick =
        AccessTools.MethodDelegate<Action<ResourceRoot>>(AccessTools.Method(typeof(ResourceRoot), "UpdateTick"));

    // What fit in the extractor, for the root's Drain that follows IncreseLevel in SapCollector.UpdateTick.
    private static int? _fitted;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Crafting",
            "SapCapacity",
            true,
            Plugin.Synced(
                "Sap extractors hold up to SapCapacityAmount sap instead of 10, and when you come back the extractor " +
                "takes everything the ancient root under it regrew while you were away at once, up to the root's 50."));
        _amount = config.Bind(
            "Crafting",
            "SapCapacityAmount",
            50,
            Plugin.Synced(
                "Most sap an extractor holds. An ancient root holds 50 and regrows about 9 an hour, so 50 takes a full " +
                "root at once.",
                new AcceptableValueRange<int>(10, 1000)));
    }

    // Every read of the extractor's m_maxLevel: the hover's "level / max" and "full", UpdateTick's room check, the
    // clamp in IncreseLevel and the working effect in UpdateEffects (also run by RPC_UpdateEffects).
    [HarmonyPatch]
    private static class ExtractorCapacity
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SapCollector), nameof(SapCollector.GetHoverText));
            yield return AccessTools.Method(typeof(SapCollector), "UpdateTick");
            yield return AccessTools.Method(typeof(SapCollector), "RPC_UpdateEffects");
        }

        private static void Prefix(SapCollector __instance, out int? __state)
        {
            __state = __instance.m_maxLevel;
            if (_enabled.Value) __instance.m_maxLevel = Mathf.Max(__instance.m_maxLevel, _amount.Value);
        }

        // __state is null when an earlier prefix threw and this one never ran.
        private static void Finalizer(SapCollector __instance, int? __state)
        {
            if (__state != null) __instance.m_maxLevel = __state.Value;
            _fitted = null;
        }
    }

    // Called by UpdateTick on the extractor's owner only, right after it looked for the root; it saves now as the
    // extractor's lastTime. While no root is found yet or it can't be drained, the clock is left alone instead, so the
    // time away still counts once the root is there and caught up.
    [HarmonyPatch(typeof(SapCollector), "GetTimeSinceLastUpdate")]
    private static class Clock
    {
        private static bool Prefix(SapCollector __instance, ref float __result)
        {
            if (!_enabled.Value) return true;
            var root = CollectorRoot(__instance);
            if (root != null)
            {
                var rootView = RootView(root);
                if (rootView != null && rootView.IsOwner()) RootUpdateTick(root);
                if (root.CanDrain(1f)) return true;
            }
            __result = 0f;
            return false;
        }
    }

    // UpdateTick asks for min(root level, time / 60) and drains that much from the root whatever fit.
    [HarmonyPatch(typeof(SapCollector), "IncreseLevel")]
    private static class Fit
    {
        private static void Prefix(SapCollector __instance, ref int i)
        {
            if (!_enabled.Value) return;
            var root = CollectorRoot(__instance);
            if (root == null) return;
            var room = __instance.m_maxLevel - CollectorView(__instance).GetZDO().GetInt(ZDOVars.s_level);
            // Drain takes only less than the root holds, so a full root of exactly 50 gives 49. A root another game
            // controls shows a drain only once it comes back from there, so a second extractor could take the same sap
            // again; from such a root take one at a time, as a tick does while someone stands there.
            var rootView = RootView(root);
            var drainable = rootView != null && rootView.IsOwner() ? Mathf.CeilToInt(root.GetLevel()) - 1 : 1;
            i = Mathf.Max(0, Mathf.Min(i, Mathf.Min(room, drainable)));
            _fitted = i;
        }
    }

    [HarmonyPatch(typeof(ResourceRoot), nameof(ResourceRoot.Drain))]
    private static class DrainFitted
    {
        private static bool Prefix(ref float amount, ref bool __result)
        {
            if (_fitted == null) return true;
            amount = _fitted.Value;
            _fitted = null;
            if (amount > 0f) return true;
            __result = false;
            return false;
        }
    }

    // Vanilla drops one item per sap, which would scatter hundreds of drops; they come out as full stacks instead.
    // Runs on the extractor's owner, where Interact sends it.
    [HarmonyPatch(typeof(SapCollector), "RPC_Extract")]
    private static class ExtractStacks
    {
        private static bool Prefix(SapCollector __instance)
        {
            if (!_enabled.Value) return true;
            var nview = CollectorView(__instance);
            if (nview == null || !nview.IsValid()) return true;
            var level = nview.GetZDO().GetInt(ZDOVars.s_level);
            if (level <= 0) return true;

            var item = __instance.m_spawnItem;
            var maxStack = Mathf.Max(1, item.m_itemData.m_shared.m_maxStackSize);
            var position = __instance.m_spawnPoint.position;
            __instance.m_spawnEffect.Create(position, Quaternion.identity);
            for (var left = level * Game.instance.ScaleDrops(item.m_itemData, 1); left > 0; left -= maxStack)
            {
                var drop = UnityEngine.Object.Instantiate(item, position + UnityEngine.Random.insideUnitSphere * 0.2f, Quaternion.identity);
                drop.SetStack(Mathf.Min(left, maxStack));
            }
            nview.GetZDO().Set(ZDOVars.s_level, 0);
            nview.InvokeRPC(ZNetView.Everybody, "RPC_UpdateEffects");
            return false;
        }
    }
}
