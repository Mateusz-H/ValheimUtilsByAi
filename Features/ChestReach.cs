using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimUtilsByAi.Features;

// Vanilla lets you open a chest from up to 5 m, measured from the eyes to the point looked at
// (Player.m_maxInteractDistance), but closes it once its pivot, at its base, is more than 4 m from your feet
// (InventoryGui.m_autoCloseDistance, checked every frame in UpdateContainer). Opened from the edge of reach, or at the
// top of a stack of chests, a chest closed again in the same frame and its panel never showed.
//
// While this is on, the chest stays open as long as some part of it is within reach plus a margin, measured like the
// interaction, from the eyes to the nearest point of its colliders. For the duration of UpdateContainer the vanilla
// close distance is set so its own check gives that answer, and is put back afterwards.
internal static class ChestReach
{
    private const float Margin = 1f;

    private static readonly AccessTools.FieldRef<InventoryGui, Container?> CurrentContainer =
        AccessTools.FieldRefAccess<InventoryGui, Container?>("m_currentContainer");

    private static ConfigEntry<bool> _enabled = null!;
    private static Container? _chest;
    private static Collider[] _colliders = new Collider[0];

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "ChestReach",
            true,
            "A chest you can open stays open, also at the edge of reach or at the top of a stack of chests, where " +
            "vanilla closes it right away. It closes once you are about 1 m beyond reach.");
    }

    private static bool InReach(Player player, Container chest)
    {
        if (chest != _chest)
        {
            _chest = chest;
            _colliders = chest.GetComponentsInChildren<Collider>();
        }
        var eye = player.GetEyePoint();
        var reach = player.m_maxInteractDistance + Margin;
        if (_colliders.Length == 0) return Vector3.Distance(chest.transform.position, eye) <= reach;
        foreach (var collider in _colliders)
        {
            if (collider != null && collider.enabled && Vector3.Distance(collider.ClosestPointOnBounds(eye), eye) <= reach) return true;
        }
        return false;
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    private static class KeepOpenInReach
    {
        private static void Prefix(InventoryGui __instance, Player player, out float __state)
        {
            __state = __instance.m_autoCloseDistance;
            var chest = CurrentContainer(__instance);
            if (!_enabled.Value || player == null || chest == null) return;
            __instance.m_autoCloseDistance = InReach(player, chest) ? float.MaxValue : -1f;
        }

        private static void Finalizer(InventoryGui __instance, float __state) => __instance.m_autoCloseDistance = __state;
    }
}
