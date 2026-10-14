using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// A sapling, or a pickable planted on cultivated ground (FarmEverything), can't be placed where another planted thing
// (a sapling, a grown crop or a planted pickable) is within its grow radius, nor inside the grow radius of a bigger
// sapling nearby. Vanilla checks the radius only after planting (the sapling then stays "Not enough space") and only
// against colliders on the piece layers, so a sapling on top of a grown Jotun puff or a planted mushroom, whose
// colliders sit on the item layer, grows anyway. Wild pickables (stones, branches, wild mushrooms other than Jotun
// puffs and magecap, which share the grown prefab) don't count, so tree saplings still go anywhere in the wild.
// PlantEasily evaluates its grid ghosts itself, so its GhostStatus.EvaluateStatus gets the same check.
internal static class PlantSpacing
{
    // Planted pickables have no grow radius of their own.
    private const float PickableRadius = 0.5f;

    private static readonly int Mask =
        LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "item");
    private static readonly Collider[] Hits = new Collider[256];
    // Prefab names of everything a sapling grows into, and the largest grow radius of any sapling; filled on first
    // use, once every mod has registered its prefabs.
    private static HashSet<string>? _grownPrefabs;
    private static float _largestGrowRadius;
    private static ConfigEntry<bool> _enabled = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "World",
            "PlantSpacing",
            true,
            Plugin.Synced(
                "A sapling or seed can't be planted too close to another planted thing: saplings, grown crops and " +
                "planted mushrooms or bushes. Off: vanilla, which lets you plant on top of them."));
    }

    // Placement ghosts sit on the ghost layer, outside the mask, so neither the ghost nor PlantEasily's extra ghosts
    // count.
    private static bool Crowded(GameObject ghost)
    {
        var plant = ghost.GetComponent<Plant>();
        float radius;
        if (plant != null) radius = plant.m_growRadius;
        else if (ghost.GetComponent<Pickable>() != null && ghost.GetComponent<Piece>().m_cultivatedGroundOnly)
            radius = PickableRadius;
        else return false;

        if (_grownPrefabs == null)
        {
            var saplings = ZNetScene.instance.m_prefabs
                .Where(prefab => prefab != null)
                .Select(prefab => prefab.GetComponent<Plant>())
                .Where(sapling => sapling != null)
                .ToList();
            _grownPrefabs = new HashSet<string>(saplings
                .SelectMany(sapling => sapling.m_grownPrefabs)
                .Where(grown => grown != null)
                .Select(grown => grown.name));
            _largestGrowRadius = saplings.Select(sapling => sapling.m_growRadius).DefaultIfEmpty(0f).Max();
        }

        // Its own space, tested like Plant.HaveGrowSpace tests it after planting: any collider inside the sphere.
        var center = ghost.transform.position;
        var count = Physics.OverlapSphereNonAlloc(center, radius, Hits, Mask);
        for (var i = 0; i < count; i++)
        {
            if (Hits[i].GetComponentInParent<Plant>() != null) return true;
            var pickable = Hits[i].GetComponentInParent<Pickable>();
            if (pickable != null && (pickable.GetComponent<Piece>() != null ||
                                     _grownPrefabs.Contains(Utils.GetPrefabName(pickable.gameObject.name))))
                return true;
        }

        // The space of a bigger sapling nearby (an oak needs 3 m), which the new plant would keep from growing.
        if (_largestGrowRadius <= radius) return false;
        count = Physics.OverlapSphereNonAlloc(center, _largestGrowRadius, Hits, Mask);
        for (var i = 0; i < count; i++)
        {
            var sapling = Hits[i].GetComponentInParent<Plant>();
            if (sapling != null && sapling.m_growRadius > radius &&
                (sapling.transform.position - center).sqrMagnitude < sapling.m_growRadius * sapling.m_growRadius)
                return true;
        }
        return false;
    }

    // Runs every frame for the cursor ghost and again in TryPlacePiece, which then shows vanilla's "Needs more space".
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    private static class CursorGhost
    {
        private static void Postfix(GameObject ___m_placementGhost, ref Player.PlacementStatus ___m_placementStatus)
        {
            if (!_enabled.Value || ___m_placementGhost == null) return;
            if (___m_placementStatus != Player.PlacementStatus.Valid || !Crowded(___m_placementGhost)) return;
            ___m_placementStatus = Player.PlacementStatus.MoreSpace;
            ___m_placementGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(true);
        }
    }

    // PlantEasily's status of every grid ghost, the cursor one included: the highlight, which extra ghosts get
    // planted and its own placement check all read it. Its Status enum is internal, so values are compared boxed.
    [HarmonyPatch]
    private static class PlantEasilyGhost
    {
        private const string Guid = "advize.PlantEasily";
        private static object _healthy = null!;
        private static object _lackResources = null!;
        private static object _noSpace = null!;
        private static MethodBase? _target;
        private static bool _searched;

        // PlantEasily loads first when installed: it is a soft dependency of Plugin.
        private static bool Prepare()
        {
            if (_searched) return _target != null;
            _searched = true;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return false;
            var method = AccessTools.Method(
                info.Instance.GetType().Assembly.GetType("Advize_PlantEasily.GhostStatus"), "EvaluateStatus");
            var status = method?.ReturnType;
            if (status == null || !status.IsEnum || new[] { "Healthy", "LackResources", "NoSpace" }.Any(
                    name => !Enum.IsDefined(status, name)))
            {
                Plugin.Log.LogWarning($"PlantSpacing: unknown PlantEasily {info.Metadata.Version}, its grid is not checked");
                return false;
            }
            _healthy = Enum.Parse(status, "Healthy");
            _lackResources = Enum.Parse(status, "LackResources");
            _noSpace = Enum.Parse(status, "NoSpace");
            _target = method;
            return true;
        }

        private static MethodBase TargetMethod() => _target!;

        private static void Postfix(GameObject ghost, ref object __result)
        {
            if (!_enabled.Value || (!__result.Equals(_healthy) && !__result.Equals(_lackResources))) return;
            if (Crowded(ghost)) __result = _noSpace;
        }
    }
}
