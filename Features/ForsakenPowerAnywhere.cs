using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimUtilsByAi.Features;

[HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
internal static class ForsakenPowerAnywhere
{
    // Same order as the guardian stones in ItemStand.DelayedPowerActivation, i.e. boss progression.
    private static readonly string[] Powers =
        { "GP_Eikthyr", "GP_TheElder", "GP_Bonemass", "GP_Moder", "GP_Yagluth", "GP_Queen", "GP_Ashlands", "GP_DeepNorth" };

    // The button is always English, whatever the game language.
    private static readonly Dictionary<string, string> EnglishNames = new()
    {
        ["GP_Eikthyr"] = "Eikthyr", ["GP_TheElder"] = "The Elder", ["GP_Bonemass"] = "Bonemass", ["GP_Moder"] = "Moder",
        ["GP_Yagluth"] = "Yagluth", ["GP_Queen"] = "The Queen", ["GP_Ashlands"] = "Fader", ["GP_DeepNorth"] = "Deep North",
    };

    // Vanilla stones check that their trophy is placed, which a client can't see from afar; the world key set by the
    // boss kill is the closest per-world check. FrozenKing sets defeated_frozenking after phase one, _p3 on the kill.
    private static readonly Dictionary<string, string> BossKeys = new()
    {
        ["GP_Eikthyr"] = "defeated_eikthyr", ["GP_TheElder"] = "defeated_gdking", ["GP_Bonemass"] = "defeated_bonemass",
        ["GP_Moder"] = "defeated_dragon", ["GP_Yagluth"] = "defeated_goblinking", ["GP_Queen"] = "defeated_queen",
        ["GP_Ashlands"] = "defeated_fader", ["GP_DeepNorth"] = "defeated_frozenking_p3",
    };

    private static ConfigEntry<bool> _enabled = null!;
    private static Button? _button;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Combat",
            "ForsakenPowerAnywhere",
            true,
            "Adds a button to the Escape menu that switches your Forsaken power to the next one you already picked at " +
            "a guardian stone, so you don't have to walk back to the stones. The cooldown and any running power are not affected.");
    }

    // The button is a copy of Settings, created once per menu instance and shown or hidden on every Show.
    private static void Postfix(Menu __instance)
    {
        var player = Player.m_localPlayer;
        var show = _enabled.Value && player != null && KnownPowers(player).Count > 0;
        if (_button == null)
        {
            if (!show) return;
            var settings = __instance.m_settingsButton;
            _button = Object.Instantiate(settings, settings.transform.parent);
            _button.name = "ForsakenPowerButton";
            _button.transform.SetSiblingIndex(settings.transform.GetSiblingIndex() + 1);
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(Cycle);
        }
        _button.gameObject.SetActive(show);
        if (show) UpdateLabel(player!);
    }

    // Player.SetGuardianPower records every power picked at a stone as a player unique key; only those are offered.
    // SetGuardianPower itself leaves m_guardianPowerCooldown and active status effects alone, exactly like the stone.
    private static void Cycle()
    {
        var player = Player.m_localPlayer;
        if (!_enabled.Value || player == null) return;
        var known = KnownPowers(player);
        if (known.Count == 0) return;
        player.SetGuardianPower(known[(known.IndexOf(player.GetGuardianPowerName()) + 1) % known.Count]);
        UpdateLabel(player);
    }

    private static List<string> KnownPowers(Player player) =>
        Powers.Where(p => player.HaveUniqueKey(p) && ZoneSystem.instance != null &&
                          ZoneSystem.instance.GetGlobalKey(BossKeys[p]) &&
                          ObjectDB.instance.GetStatusEffect(p.GetStableHashCode())).ToList();

    private static void UpdateLabel(Player player)
    {
        EnglishNames.TryGetValue(player.GetGuardianPowerName(), out var name);
        _button!.GetComponentInChildren<TMP_Text>().text = "Forsaken power: " + (name ?? "-");
    }
}
