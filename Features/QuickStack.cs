using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ValheimUtilsByAi.Features;

// P (or the Q button under the weight) puts stackable items from the inventory into nearby chests that already hold
// that item: nearest chest first, then the next chest holding it once that one is full. Items no chest holds stay, and
// the message says how many went, e.g. "Stored 12 of 23 items". With a chest open, P and the Quick stack button put
// them into that chest only. Vanilla's Place stacks and hold-E stacking (both Inventory.StackAll) follow the same rules.
// Partial stacks of the same item in a chest are merged afterwards.
//
// Never moved: the hotbar row, equipped items, quest items and liked stacks (LikedItems).
//
// Chests are those CraftFromContainers may use (built by a player, accessible, not carts, ships or tombstones). In
// multiplayer a chest is only edited while this game owns it: chests holding one of the items are asked for the way
// CraftFromContainers asks (the vanilla open request with the chest UI suppressed) and the items move once they arrive
// or after a short timeout; a chest another player has open counts as busy.
//
// Also home of what StoreAll, Restock and SortItems share: moving and merging stacks, the hotkey gate, the chest
// hand-over and the buttons in the inventory and container panels.
internal static class QuickStack
{
    private const float ChestTimeout = 2f;

    private static readonly AccessTools.FieldRef<InventoryGui, Container?> CurrentContainer =
        AccessTools.FieldRefAccess<InventoryGui, Container?>("m_currentContainer");
    private static readonly Action<InventoryGui, ItemDrop.ItemData?, Inventory?, int> SetupDragItem =
        AccessTools.MethodDelegate<Action<InventoryGui, ItemDrop.ItemData?, Inventory?, int>>(
            AccessTools.Method(typeof(InventoryGui), "SetupDragItem"));
    private static readonly Action<InventoryGui> HideSplitDialog =
        AccessTools.MethodDelegate<Action<InventoryGui>>(AccessTools.Method(typeof(InventoryGui), "HideSplitDialog"));
    private static readonly Action<InventoryGui, bool> UpdateCraftingPanel =
        AccessTools.MethodDelegate<Action<InventoryGui, bool>>(AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));
    private static readonly Action<Inventory, bool, bool> InventoryChanged =
        AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(AccessTools.Method(typeof(Inventory), "Changed"));
    private static readonly Func<Inventory, bool, Vector2i> FindEmptySlot =
        AccessTools.MethodDelegate<Func<Inventory, bool, Vector2i>>(AccessTools.Method(typeof(Inventory), "FindEmptySlot"));
    private static readonly Func<Inventory, ItemDrop.ItemData, bool> TopFirst =
        AccessTools.MethodDelegate<Func<Inventory, ItemDrop.ItemData, bool>>(AccessTools.Method(typeof(Inventory), "TopFirst"));

    private static ConfigEntry<bool> _enabled = null!;
    private static ConfigEntry<KeyboardShortcut> _key = null!;
    private static ConfigEntry<float> _radius = null!;

    public static void Bind(ConfigFile config)
    {
        _enabled = config.Bind(
            "Inventory",
            "QuickStack",
            true,
            "QuickStackKey (or the Q button under your weight) puts stackable items from your inventory into nearby " +
            "chests that already hold the same item, nearest first, and merges partial stacks there. With a chest " +
            "open it, the Quick stack button and vanilla's Place stacks fill that chest only. The hotbar, equipped " +
            "and liked items stay.");
        _key = config.Bind("Inventory", "QuickStackKey", new KeyboardShortcut(KeyCode.P),
            "Key for quick stacking. Works with the inventory closed too.");
        _radius = config.Bind(
            "Inventory",
            "QuickStackRadius",
            10f,
            new ConfigDescription("How far from you, in meters, a chest can be for quick stacking.",
                new AcceptableValueRange<float>(1f, 50f)));

        AddMiniButton("VU_QuickStackArea", "Q", () => _enabled.Value && OpenChest() == null, StackNearby);
        AddContainerButton("VU_QuickStackContainer", "Quick stack", () => _enabled.Value && OpenChest() != null,
            StackIntoOpenChest, aboveTakeAll: true);
    }

    // ---------------------------------------------------------------- quick stacking

    private static void Run()
    {
        var gui = InventoryGui.instance;
        if (gui != null && gui.IsContainerOpen()) StackIntoOpenChest();
        else StackNearby();
    }

    private static void StackIntoOpenChest()
    {
        var player = Player.m_localPlayer;
        var chest = OpenChest();
        if (player == null || chest == null) return;
        Store(player, new List<Inventory> { chest.GetInventory() }, 0, everything: false);
        if (Game.instance != null) Game.instance.IncrementPlayerStat(PlayerStatType.PlaceStacks);
    }

    private static void StackNearby()
    {
        var player = Player.m_localPlayer;
        if (player == null || !_enabled.Value) return;
        var names = new HashSet<string>();
        foreach (var item in player.GetInventory().GetAllItems())
            if (Stackable(item) && CanMove(player, item)) names.Add(item.m_shared.m_name);
        if (names.Count == 0)
        {
            player.Message(MessageHud.MessageType.Center, "Nothing to quick stack");
            return;
        }
        var chests = NearbyChests(_radius.Value, inventory => inventory.GetAllItems().Any(i => names.Contains(i.m_shared.m_name)));
        WithChests(chests, (owned, busy) =>
        {
            var p = Player.m_localPlayer;
            if (p != null) Store(p, owned.ConvertAll(CraftFromContainers.LoadedInventory), busy, everything: false);
        });
    }

    // Vanilla's Place stacks button and hold-E stacking, with quick stack's rules. Both only run while this game owns
    // the chest: the container panel is only shown then, and hold-E stacks on the owner's grant.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
    private static class StackLikeQuickStack
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, ref int __result)
        {
            var player = Player.m_localPlayer;
            if (!_enabled.Value || player == null || fromInventory != player.GetInventory()) return true;
            __result = Store(player, new List<Inventory> { __instance }, 0, everything: false);
            if (Game.instance != null) Game.instance.IncrementPlayerStat(PlayerStatType.PlaceStacks);
            return false;
        }
    }

    // Moves the player's movable stackable items into the chests in order, each only into chests already holding an
    // item of that name, merges the chests' partial stacks and reports. With everything (StoreAll) every movable item
    // goes, into any chest.
    internal static int Store(Player player, List<Inventory> chests, int busy, bool everything)
    {
        CancelDrag();
        var inventory = player.GetInventory();
        var items = inventory.GetAllItems()
            .Where(i => (everything || Stackable(i)) && CanMove(player, i))
            .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x)
            .ToList();
        var total = items.Sum(i => i.m_stack);
        var moved = 0;
        foreach (var chest in chests)
        {
            var any = false;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!everything && !chest.ContainsItemByName(item.m_shared.m_name)) continue;
                var count = Move(player, inventory, item, chest, item.m_stack);
                if (count == 0) continue;
                moved += count;
                any = true;
                if (!inventory.ContainsItem(item)) items.RemoveAt(i--);
            }
            if (any) MergeStacks(chest, item => !LikedItems.IsLiked(chest, item), _ => true);
        }
        var text = total == 0 ? everything ? "Nothing to store" : "Nothing to quick stack"
            : moved == total ? $"Stored {Items(total)}"
            : $"Stored {moved} of {Items(total)}";
        player.Message(MessageHud.MessageType.Center, text + Busy(busy));
        RefreshCrafting();
        return moved;
    }

    internal static string Items(int count) => count == 1 ? "1 item" : $"{count} items";

    internal static string Busy(int busy) => busy == 0 ? "" : $" ({busy} {(busy == 1 ? "chest" : "chests")} busy)";

    // ---------------------------------------------------------------- moving and merging stacks

    internal static bool Stackable(ItemDrop.ItemData item) => item.m_shared.m_maxStackSize > 1;

    // Quick stack and store all never move the hotbar row, equipped or quest items, or liked stacks.
    internal static bool CanMove(Player player, ItemDrop.ItemData item) =>
        item.m_gridPos.y > 0 && !item.m_equipped && !player.IsItemEquiped(item) && !item.m_shared.m_questItem &&
        !LikedItems.IsLiked(player.GetInventory(), item);

    // Inventory.FindFreeStackItem's rule for stacks AddItem merges.
    internal static bool SameStack(ItemDrop.ItemData a, ItemDrop.ItemData b) =>
        a.m_shared.m_name == b.m_shared.m_name && a.m_quality == b.m_quality && a.m_worldLevel == b.m_worldLevel;

    internal static void Changed(Inventory inventory) => InventoryChanged(inventory, false, false);

    // Moves up to amount units of item from one inventory to another: first into stacks it merges with, then into
    // empty slots, picked the way AddItem does. New stacks are copies, and drop a like, which doesn't carry over. The
    // source loses exactly what the target gained, and both save. Returns the units moved. Not Inventory.AddItem: it
    // can add units without saying so and corrupts the list when the item is already in the target.
    internal static int Move(Player player, Inventory from, ItemDrop.ItemData item, Inventory to, int amount)
    {
        amount = Mathf.Min(amount, item.m_stack);
        if (amount <= 0 || from == to) return 0;
        var max = Mathf.Max(1, item.m_shared.m_maxStackSize);
        var moved = 0;
        var cheated = item.m_cheated && !PlayerProfile.s_bypassCheatChecks;
        if (max > 1)
        {
            foreach (var stack in to.GetAllItems())
            {
                if (moved >= amount) break;
                if (!SameStack(stack, item) || stack.m_stack >= max) continue;
                var count = Mathf.Min(max - stack.m_stack, amount - moved);
                stack.m_stack += count;
                if (cheated) stack.m_cheated = true;
                moved += count;
            }
        }
        while (moved < amount)
        {
            var slot = FindEmptySlot(to, TopFirst(to, item));
            if (slot.x < 0) break;
            var copy = item.Clone();
            copy.m_stack = Mathf.Min(max, amount - moved);
            copy.m_gridPos = slot;
            copy.m_equipped = false;
            LikedItems.Unlike(copy);
            to.GetAllItems().Add(copy);
            moved += copy.m_stack;
        }
        if (moved == 0) return 0;
        // The sheathed weapon references and queued equips go, as when vanilla drops an item.
        if (from == player.GetInventory() && moved >= item.m_stack)
        {
            player.RemoveEquipAction(item);
            player.UnequipItem(item, triggerEquipEffects: false);
        }
        if (moved >= item.m_stack) from.RemoveItem(item);
        else
        {
            item.m_stack -= moved;
            Changed(from);
        }
        Changed(to);
        return moved;
    }

    // Pours partial stacks of the same item into each other: stacks that can only take (liked chest stacks) first, then
    // from the top left, taking from the bottom right; emptied stacks are removed. Equipped and liked stacks are kept
    // out by the callers' predicates.
    internal static bool MergeStacks(Inventory inventory, Func<ItemDrop.ItemData, bool> canGive, Func<ItemDrop.ItemData, bool> canTake)
    {
        var items = inventory.GetAllItems();
        var merged = false;
        var groups = items.Where(i => Stackable(i) && (canGive(i) || canTake(i)))
            .GroupBy(i => $"{i.m_shared.m_name}\n{i.m_quality}\n{i.m_worldLevel}")
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in groups)
        {
            var stacks = group.OrderBy(i => canGive(i) ? 1 : 0).ThenBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
            int first = 0, last = stacks.Count - 1;
            while (first < last)
            {
                var target = stacks[first];
                var max = target.m_shared.m_maxStackSize;
                if (!canTake(target) || target.m_stack >= max) { first++; continue; }
                var source = stacks[last];
                if (!canGive(source) || source.m_stack <= 0) { last--; continue; }
                var count = Mathf.Min(max - target.m_stack, source.m_stack);
                target.m_stack += count;
                source.m_stack -= count;
                if (source.m_cheated && !PlayerProfile.s_bypassCheatChecks) target.m_cheated = true;
                merged = true;
            }
        }
        if (!merged) return false;
        items.RemoveAll(i => i.m_stack <= 0);
        Changed(inventory);
        return true;
    }

    // A running drag or split points at an item and an amount; moving or shrinking that item under it would let the
    // drop duplicate it.
    internal static void CancelDrag()
    {
        var gui = InventoryGui.instance;
        if (gui == null) return;
        HideSplitDialog(gui);
        SetupDragItem(gui, null, null, 1);
    }

    internal static void RefreshCrafting()
    {
        var gui = InventoryGui.instance;
        if (gui != null && InventoryGui.IsVisible()) UpdateCraftingPanel(gui, false);
    }

    // The open chest when this game owns it and it isn't a tombstone; null otherwise.
    internal static Container? OpenChest()
    {
        var gui = InventoryGui.instance;
        var chest = gui != null ? CurrentContainer(gui) : null;
        if (chest == null || !CraftFromContainers.IsOwned(chest) || chest.GetComponent<TombStone>() != null) return null;
        return chest;
    }

    // ---------------------------------------------------------------- chests nearby

    // Chests CraftFromContainers may use within radius, nearest first, whose current contents pass the filter.
    internal static List<Container> NearbyChests(float radius, Func<Inventory, bool> wanted)
    {
        var chests = new List<Container>();
        CraftFromContainers.UsableChestsWithin(radius, chests);
        var player = Player.m_localPlayer;
        if (player == null) return chests;
        var position = player.transform.position;
        chests.RemoveAll(chest => !wanted(CraftFromContainers.LoadedInventory(chest)));
        chests.Sort((a, b) => Vector3.Distance(a.transform.position, position).CompareTo(Vector3.Distance(b.transform.position, position)));
        return chests;
    }

    // A class, not a tuple: the game's runtime has no System.ValueTuple.
    private sealed class ChestJob
    {
        public readonly List<Container> Chests;
        public readonly List<Container> Waiting;
        public readonly Action<List<Container>, int> Run;
        public readonly int Busy;
        public readonly float Deadline;

        public ChestJob(List<Container> chests, List<Container> waiting, Action<List<Container>, int> run, int busy, float deadline)
        {
            Chests = chests;
            Waiting = waiting;
            Run = run;
            Busy = busy;
            Deadline = deadline;
        }
    }

    private static ChestJob? _job;

    // Asks for the chests this game doesn't own and runs run once all of them arrived, or after a timeout, with those
    // it owns then (in the given order) and how many it couldn't use. One job at a time; a key press during one is
    // ignored.
    internal static void WithChests(List<Container> chests, Action<List<Container>, int> run)
    {
        if (_job != null) return;
        var usable = new List<Container>();
        var waiting = new List<Container>();
        var busy = 0;
        foreach (var chest in chests)
        {
            if (CraftFromContainers.IsOwned(chest)) usable.Add(chest);
            else if (CraftFromContainers.RequestChest(chest)) { usable.Add(chest); waiting.Add(chest); }
            else busy++;
        }
        _job = new ChestJob(usable, waiting, run, busy, Time.time + ChestTimeout);
        TickJob();
    }

    private static void TickJob()
    {
        var job = _job;
        if (job == null) return;
        if (Time.time < job.Deadline &&
            job.Waiting.Exists(c => c != null && !CraftFromContainers.IsOwned(c) && !CraftFromContainers.Busy(c))) return;
        _job = null;
        if (Player.m_localPlayer == null) return;
        var owned = job.Chests.FindAll(c => c != null && CraftFromContainers.IsOwned(c));
        job.Run(owned, job.Busy + job.Chests.Count - owned.Count);
    }

    // ---------------------------------------------------------------- hotkeys

    // ZInput, not KeyboardShortcut.IsDown: that refuses while any other key is held, such as W while walking.
    internal static bool Pressed(KeyboardShortcut shortcut) =>
        shortcut.MainKey != KeyCode.None && ZInput.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(m => ZInput.GetKey(m));

    // Not while typing, in a menu or popup, on the map, building, dead or teleporting.
    internal static bool KeysAllowed(Player player)
    {
        if (player != Player.m_localPlayer || player.IsDead() || player.InCutscene() || player.IsTeleporting() || player.InPlaceMode()) return false;
        if (Minimap.IsOpen() || Menu.IsVisible() || Console.IsVisible() || TextInput.IsVisible() || StoreGui.IsVisible()) return false;
        if (Chat.instance != null && Chat.instance.HasFocus()) return false;
        if (TextViewer.instance != null && TextViewer.instance.IsVisible()) return false;
        return Hud.instance == null || !Hud.instance.m_buildUi.SearchFieldFocused;
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class Keys
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            TickJob();
            if (_enabled.Value && Pressed(_key.Value) && KeysAllowed(__instance)) Run();
        }
    }

    // ---------------------------------------------------------------- buttons, shared with StoreAll, Restock and SortItems

    private sealed class PanelButton
    {
        public readonly string Name;
        public readonly string Label;
        public readonly Func<bool> Shown;
        public readonly Action OnClick;
        public readonly bool AboveTakeAll;
        public Button? Button;

        public PanelButton(string name, string label, Func<bool> shown, Action onClick, bool aboveTakeAll)
        {
            Name = name;
            Label = label;
            Shown = shown;
            OnClick = onClick;
            AboveTakeAll = aboveTakeAll;
        }
    }

    private static readonly List<PanelButton> MiniButtons = new();
    private static readonly List<PanelButton> ContainerButtons = new();

    // A 38 px square button in the inventory panel, in a row under the weight, as QuickStackStore places them.
    internal static void AddMiniButton(string name, string label, Func<bool> shown, Action onClick) =>
        MiniButtons.Add(new PanelButton(name, label, shown, onClick, false));

    // A button in the column QuickStackStore builds from Take all in the container panel.
    internal static void AddContainerButton(string name, string label, Func<bool> shown, Action onClick, bool aboveTakeAll = false) =>
        ContainerButtons.Add(new PanelButton(name, label, shown, onClick, aboveTakeAll));

    private static float _takeAllWidth = -1f;
    private static Vector3 _takeAllPosition;

    // The layout is QuickStackStore's default one, so it lands where players of that mod know it: the mini buttons
    // 40 px apart, 56 px under "Weight"; Take all shrunk to 90 % and moved, with the container buttons, into one
    // column 440 px right of where Take all was. Vanilla's Place stacks is hidden while Quick stack replaces it.
    // Applied every frame, so toggling any of the features moves the buttons right away.
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    private static class LayOutButtons
    {
        private static void Postfix(InventoryGui __instance)
        {
            if (!InventoryGui.IsVisible() || __instance.m_takeAllButton == null) return;
            try
            {
                LayOutMiniButtons(__instance);
                LayOutContainerButtons(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"QuickStack buttons: {e}");
                MiniButtons.Clear();
                ContainerButtons.Clear();
            }
        }
    }

    private static void LayOutMiniButtons(InventoryGui gui)
    {
        var anchor = gui.m_player.Find("Weight");
        var index = 0;
        foreach (var entry in MiniButtons)
        {
            var shown = anchor != null && entry.Shown();
            if (entry.Button == null)
            {
                if (!shown) continue;
                entry.Button = CreateButton(gui, entry, gui.m_player);
                var rect = (RectTransform)entry.Button.transform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 38f);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 38f);
            }
            entry.Button.gameObject.SetActive(shown);
            if (!shown) continue;
            SetPosition(entry.Button.transform, anchor!.localPosition + new Vector3(1f + 40f * index, -56f));
            index++;
        }
    }

    // Layout is applied every frame; writing unchanged values would still rebuild the canvas.
    private static void SetPosition(Transform transform, Vector3 position)
    {
        if (transform.localPosition != position) transform.localPosition = position;
    }

    private static void SetWidth(RectTransform rect, float width)
    {
        if (Mathf.Abs(rect.rect.width - width) > 0.01f) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    private static void LayOutContainerButtons(InventoryGui gui)
    {
        var takeAll = (RectTransform)gui.m_takeAllButton.transform;
        // Taken once, before anything moved it; the panel prefab is the same in every session.
        if (_takeAllWidth <= 0f)
        {
            _takeAllWidth = takeAll.rect.width;
            _takeAllPosition = takeAll.localPosition;
            if (_takeAllWidth <= 0f) return;
        }
        var shown = ContainerButtons.FindAll(b => b.Shown());
        foreach (var entry in ContainerButtons)
        {
            if (entry.Button != null && !shown.Contains(entry)) entry.Button.gameObject.SetActive(false);
        }
        if (gui.m_stackAllButton != null) gui.m_stackAllButton.gameObject.SetActive(!shown.Exists(b => b.AboveTakeAll));
        if (shown.Count == 0)
        {
            SetWidth(takeAll, _takeAllWidth);
            SetPosition(takeAll, _takeAllPosition);
            return;
        }
        SetWidth(takeAll, _takeAllWidth * 0.9f);
        var step = takeAll.rect.height + 8f;
        var column = _takeAllPosition + new Vector3(440f + _takeAllWidth * 1.05f - 1f, 0f);
        var row = 0;
        foreach (var entry in shown.Where(b => b.AboveTakeAll)) Place(gui, entry, column, step, row++);
        SetPosition(takeAll, column - new Vector3(0f, step * row++));
        foreach (var entry in shown.Where(b => !b.AboveTakeAll)) Place(gui, entry, column, step, row++);
    }

    private static void Place(InventoryGui gui, PanelButton entry, Vector3 column, float step, int row)
    {
        if (entry.Button == null) entry.Button = CreateButton(gui, entry, gui.m_takeAllButton.transform.parent);
        SetWidth((RectTransform)entry.Button.transform, _takeAllWidth * 0.9f);
        SetPosition(entry.Button.transform, column - new Vector3(0f, step * row));
        entry.Button.gameObject.SetActive(true);
    }

    // A copy of Take all with its own label and click. Take all's persistent click is switched off rather than
    // onClick replaced, which would also drop the click sound ButtonSfx adds in OnEnable; its gamepad shortcut goes,
    // so one press doesn't fire both buttons.
    private static Button CreateButton(InventoryGui gui, PanelButton entry, Transform parent)
    {
        var button = UnityEngine.Object.Instantiate(gui.m_takeAllButton, parent);
        button.name = entry.Name;
        foreach (var pad in button.GetComponentsInChildren<UIGamePad>(true))
        {
            if (pad.m_hint != null && pad.m_hint.transform.IsChildOf(button.transform)) UnityEngine.Object.Destroy(pad.m_hint);
            UnityEngine.Object.Destroy(pad);
        }
        for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        var onClick = entry.OnClick;
        button.onClick.AddListener(() =>
        {
            var player = Player.m_localPlayer;
            if (player != null && !player.IsTeleporting()) onClick();
        });
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = entry.Label;
        return button;
    }
}
