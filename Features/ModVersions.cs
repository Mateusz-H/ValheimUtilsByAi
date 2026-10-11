using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ValheimUtilsByAi.Features;

// FejdStartup is rebuilt on every return to the main menu and SetupGui (called once, from Start) writes the vanilla
// "Version ..." label. A postfix adds a line with this mod's version and the plugin count, and copies the Settings
// button into a Mods button. Its panel lives in Jötunn's CustomGUIFront, like Jötunn's own compatibility window.
// While the panel is open the menu list is hidden, as vanilla does for the cinematics list, so Enter and the arrow
// keys can't start a game behind it. No text here contains '$': FejdStartup.Update re-localizes cached texts every frame.
internal static class ModVersions
{
    private const float Width = 520f;
    private const float Height = 640f;

    private static readonly AccessTools.FieldRef<FejdStartup, Button[]> MenuButtons =
        AccessTools.FieldRefAccess<FejdStartup, Button[]>("m_menuButtons");

    private static ConfigEntry<bool> _enabled = null!;
    private static Button? _settings;
    private static Button? _button;
    private static Selectable? _below;
    private static GameObject? _panel;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Interface",
            "ModVersions",
            true,
            "Shows this mod's version and the number of installed mods under the game version in the main menu, " +
            "and adds a Mods button there that lists every installed mod with its version.");
        _enabled.SettingChanged += (_, _) => Apply();
    }

    private static void Apply()
    {
        var startup = FejdStartup.instance;
        if (startup == null) return;
        var line = $"\n{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} ({Chainloader.PluginInfos.Count} mods)";
        var label = startup.m_versionLabel;
        label.text = label.text.Replace(line, "") + (_enabled.Value ? line : "");
        if (_button != null)
        {
            _button.gameObject.SetActive(_enabled.Value);
            Link(startup, _enabled.Value);
        }
        if (!_enabled.Value) ClosePanel();
    }

    [HarmonyPatch(typeof(FejdStartup), "SetupGui")]
    private static class AddToMainMenu
    {
        // An exception here would abort the rest of FejdStartup.Start and leave a broken main menu.
        private static void Postfix(FejdStartup __instance)
        {
            if (GUIManager.IsHeadless()) return;
            try
            {
                CreateButton(__instance);
                Apply();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"ModVersions: {e}");
            }
        }
    }

    // A copy of Settings with its persistent OnButtonSettings call switched off (replacing onClick would also drop
    // the click sound ButtonSfx registered in OnEnable) and without its key or gamepad shortcut, which would
    // otherwise press both buttons at once.
    private static void CreateButton(FejdStartup startup)
    {
        _button = null;
        _settings = startup.m_menuList.GetComponentsInChildren<Button>(true).FirstOrDefault(b =>
            Enumerable.Range(0, b.onClick.GetPersistentEventCount())
                .Any(i => b.onClick.GetPersistentMethodName(i) == nameof(FejdStartup.OnButtonSettings)));
        if (_settings == null || _settings.GetComponentInChildren<TMP_Text>(true) == null)
        {
            Plugin.Log.LogWarning("ModVersions: no Settings button in the main menu, the Mods button was not added");
            return;
        }
        var button = Object.Instantiate(_settings, _settings.transform.parent);
        button.name = "ModVersionsButton";
        button.transform.SetSiblingIndex(_settings.transform.GetSiblingIndex() + 1);
        foreach (var pad in button.GetComponentsInChildren<UIGamePad>(true))
        {
            if (pad.m_hint != null && pad.m_hint.transform.IsChildOf(button.transform)) Object.Destroy(pad.m_hint);
            Object.Destroy(pad);
        }
        for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        button.onClick.AddListener(OpenPanel);
        button.GetComponentInChildren<TMP_Text>(true).text = "Mods";
        _below = _settings.navigation.selectOnDown;
        var nav = button.navigation;
        nav.selectOnUp = _settings;
        nav.selectOnDown = _below;
        button.navigation = nav;
        _button = button;
    }

    // The arrow keys walk m_menuButtons and the gamepad follows each button's explicit up/down, so the Mods button is
    // linked in after Settings while it is shown and unlinked while it is hidden.
    private static void Link(FejdStartup startup, bool on)
    {
        var buttons = MenuButtons(startup)?.Where(b => b != _button).ToList();
        var at = buttons?.IndexOf(_settings!) ?? -1;
        if (at < 0) return;
        if (on) buttons!.Insert(at + 1, _button!);
        MenuButtons(startup) = buttons!.ToArray();
        if (_settings!.navigation.mode != Navigation.Mode.Explicit) return;
        var nav = _settings.navigation;
        nav.selectOnDown = on ? _button : _below;
        _settings.navigation = nav;
        if (_below == null) return;
        nav = _below.navigation;
        nav.selectOnUp = on ? _button : _settings;
        _below.navigation = nav;
    }

    private static void OpenPanel()
    {
        var startup = FejdStartup.instance;
        if (!_enabled.Value || startup == null || _panel != null || GUIManager.CustomGUIFront == null) return;

        var gui = GUIManager.Instance;
        var mods = Chainloader.PluginInfos.Values.Select(p => p.Metadata)
            .OrderBy(m => m.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
        var center = new Vector2(0.5f, 0.5f);
        var top = new Vector2(0.5f, 1f);
        var bottom = new Vector2(0.5f, 0f);
        _panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, center, center, Vector2.zero, Width, Height, false);

        var title = gui.CreateText($"Installed mods ({mods.Count})", _panel.transform, top, top, new Vector2(0f, -45f),
            gui.AveriaSerifBold, 26, gui.ValheimOrange, true, Color.black, Width - 60f, 40f, false);
        title.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;

        // One text per mod: a single outlined text hits Unity's 65000 vertex limit at about 3000 characters.
        const float handle = 8f, border = 10f;
        var scroll = gui.CreateScrollView(_panel.transform, false, true, handle, border, gui.ValheimScrollbarHandleColorBlock,
            new Color(0.1568628f, 0.1019608f, 0.0627451f, 1f), Width - 60f, Height - 180f);
        var content = scroll.transform.Find("Scroll View/Viewport/Content");
        var version = ColorUtility.ToHtmlStringRGB(gui.ValheimOrange);
        foreach (var m in mods)
            gui.CreateText($"{m.Name}  <color=#{version}>{m.Version}</color>", content, new Vector2(0f, 1f),
                new Vector2(0f, 1f), Vector2.zero, gui.AveriaSerifBold, 18, gui.ValheimBeige, true, Color.black,
                Width - 60f - 2 * border - handle, 0f, false);

        var copy = gui.CreateButton("Copy", _panel.transform, bottom, bottom, new Vector2(-90f, 50f), 160f, 40f);
        copy.GetComponent<Button>().onClick.AddListener(() =>
        {
            GUIUtility.systemCopyBuffer = $"Valheim {Version.GetVersionString()}\n" +
                                          string.Join("\n", mods.Select(m => $"{m.Name} {m.Version}"));
            copy.GetComponentInChildren<Text>().text = "Copied";
        });
        var close = gui.CreateButton("Close", _panel.transform, bottom, bottom, new Vector2(90f, 50f), 160f, 40f);
        close.GetComponent<Button>().onClick.AddListener(ClosePanel);
        close.GetComponent<Button>().Select();
        startup.m_menuList.SetActive(false);
    }

    private static void ClosePanel()
    {
        if (_panel == null) return;
        Object.Destroy(_panel);
        _panel = null;
        var startup = FejdStartup.instance;
        if (startup != null) startup.m_menuList.SetActive(true);
    }

    // Escape or gamepad B closes the panel; so does any vanilla flow that leaves the main menu (a Steam invite opens
    // the character select), so the panel never sits on top of another screen.
    [HarmonyPatch(typeof(FejdStartup), "Update")]
    private static class CloseOnBack
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (_panel == null) return;
            if (!__instance.m_mainMenu.activeInHierarchy || ZInput.GetKeyDown(KeyCode.Escape) ||
                ZInput.GetButtonDown("JoyButtonB"))
                ClosePanel();
        }
    }
}
