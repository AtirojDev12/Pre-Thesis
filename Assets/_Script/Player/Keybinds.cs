using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Every rebindable button in the game (Settings > Controls).</summary>
public enum GameAction
{
    MoveForward, MoveBack, MoveLeft, MoveRight, Sprint, Crouch,
    Interact, Chant, ReloadOrDiscard, ThrowOrDrop, PlayerList, FreeMouse,
    Hotbar1, Hotbar2, Hotbar3, Hotbar4,
    FlashlightToggle, FlashlightToggleMouse, FlashlightCrank,
    EatSnack, FeedFriend,
    WalkieTalk, WalkiePower,
    ReadMap, // 24 Oct
    Shout,   // 31 Oct: downed players only
}

/// <summary>
/// 20 Oct 2026 (Mr.k): ONE place for every key the player can change.
///
/// Gameplay code asks "is Interact pressed?" (<see cref="IsPressed"/>) instead of
/// reading Keyboard.current.eKey, so a rebind in Settings works everywhere at once.
///
/// STORAGE: PlayerPrefs ("settings.bind.&lt;id&gt;"), like the other settings.
/// NOT the encrypted save: keys belong to this PC, they are not progress.
/// The two walkie keys keep their old PlayerPrefs names, so old choices survive.
///
/// FIXED KEYS (not here on purpose): Esc (menus), F1/F2/F3/F8/F9 (dev tools),
/// arrow keys on readable papers, gamepad sticks/buttons.
///
/// NO ALLOCATION per frame: paths are cached in memory, controls are resolved once.
/// </summary>
public static class Keybinds
{
    public sealed class Def
    {
        public readonly GameAction action;
        public readonly string id;          // PlayerPrefs name
        public readonly string label;       // Settings screen
        public readonly string group;       // Settings screen header
        public readonly string context;     // "world" = always active; others only with that item in hand
        public readonly string defaultPath; // "" = no key

        public Def(GameAction action, string id, string label, string group, string context, string defaultPath)
        {
            this.action = action; this.id = id; this.label = label;
            this.group = group; this.context = context; this.defaultPath = defaultPath;
        }
    }

    private const string World = "world";
    private const string PrefPrefix = "settings.bind.";
    private const string NonePath = "<None>"; // saved when the player clears a key

    // Order = order on the Settings screen. Ids must never change (saved on players' PCs).
    private static readonly Def[] defs =
    {
        new Def(GameAction.MoveForward, "moveForward", "Move forward", "Movement", World, "<Keyboard>/w"),
        new Def(GameAction.MoveBack, "moveBack", "Move back", "Movement", World, "<Keyboard>/s"),
        new Def(GameAction.MoveLeft, "moveLeft", "Move left", "Movement", World, "<Keyboard>/a"),
        new Def(GameAction.MoveRight, "moveRight", "Move right", "Movement", World, "<Keyboard>/d"),
        new Def(GameAction.Sprint, "sprint", "Sprint (hold)", "Movement", World, "<Keyboard>/leftShift"),
        new Def(GameAction.Crouch, "crouch", "Crouch (hold)", "Movement", World, "<Keyboard>/leftCtrl"),

        new Def(GameAction.Interact, "interact", "Use / interact (hold for tasks)", "Actions", World, "<Keyboard>/e"),
        new Def(GameAction.Chant, "chant", "Chant (hold)", "Actions", World, "<Keyboard>/c"),
        new Def(GameAction.ReloadOrDiscard, "reload", "New battery / discard / drop carried item", "Actions", World, "<Keyboard>/r"),
        new Def(GameAction.ThrowOrDrop, "throw", "Throw (hold) / drop (tap)", "Actions", World, "<Keyboard>/q"),
        new Def(GameAction.PlayerList, "playerList", "Players + lobby menu (ping, volume, Ready / Start / Leave)", "Actions", World, "<Keyboard>/tab"),
        new Def(GameAction.FreeMouse, "freeMouse", "Free / lock mouse", "Actions", World, ""),

        new Def(GameAction.Hotbar1, "hotbar1", "Hotbar slot 1", "Hotbar", World, "<Keyboard>/1"),
        new Def(GameAction.Hotbar2, "hotbar2", "Hotbar slot 2", "Hotbar", World, "<Keyboard>/2"),
        new Def(GameAction.Hotbar3, "hotbar3", "Hotbar slot 3", "Hotbar", World, "<Keyboard>/3"),
        new Def(GameAction.Hotbar4, "hotbar4", "Hotbar slot 4", "Hotbar", World, "<Keyboard>/4"),

        new Def(GameAction.FlashlightToggle, "flashlight", "Flashlight on / off", "Items in hand", "flashlight", "<Keyboard>/f"),
        new Def(GameAction.FlashlightToggleMouse, "flashlightMouse", "Flashlight on / off (2nd key)", "Items in hand", "flashlight", "<Mouse>/leftButton"),
        new Def(GameAction.FlashlightCrank, "crank", "Charge basic flashlight (mash)", "Items in hand", "flashlight", "<Keyboard>/space"),
        new Def(GameAction.EatSnack, "eatSnack", "Eat snack (hold)", "Items in hand", "snack", "<Mouse>/leftButton"),
        new Def(GameAction.FeedFriend, "feedFriend", "Feed a friend (hold, look at them)", "Items in hand", "snack", "<Mouse>/rightButton"),
        new Def(GameAction.WalkieTalk, "walkieTalk", "Walkie talk (hold)", "Items in hand", "walkie", GameSettings.DefaultWalkieTalkBinding),
        new Def(GameAction.WalkiePower, "walkiePower", "Walkie on / off", "Items in hand", "walkie", GameSettings.DefaultWalkiePowerBinding),
        new Def(GameAction.ReadMap, "readMap", "Read map (hold)", "Items in hand", "map", "<Mouse>/leftButton"), // 24 Oct
        new Def(GameAction.Shout, "shout", "Shout for help (when downed, once)", "Actions", World, "<Keyboard>/v"), // 31 Oct
    };

    private static string[] paths;          // cached, index = (int)GameAction
    private static BoundButton[] buttons;
    private static string[] names;          // cached short key names
    private static int version;             // +1 on every change (hint cache)

    public static IReadOnlyList<Def> All => defs;
    public static int Version => version;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        paths = null;
        buttons = null;
        names = null;
        version = 0;
        hintCache.Clear();
    }

    private static void EnsureLoaded()
    {
        if (paths != null) return;
        int count = System.Enum.GetValues(typeof(GameAction)).Length;
        paths = new string[count];
        buttons = new BoundButton[count];
        names = new string[count];
        for (int i = 0; i < count; i++) buttons[i] = new BoundButton();
        foreach (Def def in defs)
        {
            string saved = PlayerPrefs.GetString(PrefPrefix + def.id, def.defaultPath);
            paths[(int)def.action] = saved == NonePath ? string.Empty : saved;
        }
    }

    public static Def Get(GameAction action)
    {
        foreach (Def def in defs) if (def.action == action) return def;
        return null;
    }

    /// <summary>Control path of the action ("" = no key).</summary>
    public static string GetPath(GameAction action)
    {
        EnsureLoaded();
        return paths[(int)action] ?? string.Empty;
    }

    /// <summary>Change one key (written to PlayerPrefs; call GameSettings.Save() to flush).</summary>
    public static void SetPath(GameAction action, string path)
    {
        EnsureLoaded();
        Def def = Get(action);
        if (def == null) return;
        if (path == null) path = string.Empty;
        if (paths[(int)action] == path) return;
        paths[(int)action] = path;
        names[(int)action] = null;
        PlayerPrefs.SetString(PrefPrefix + def.id, path.Length == 0 ? NonePath : path);
        version++;
        hintCache.Clear();
        GameSettings.NotifyBindingsChanged();
    }

    public static void ResetAllToDefault()
    {
        foreach (Def def in defs) SetPath(def.action, def.defaultPath);
    }

    public static bool IsDefault(GameAction action)
    {
        Def def = Get(action);
        return def == null || GetPath(action) == def.defaultPath;
    }

    // ---- Reading buttons (every frame, no allocation) -----------------------------------

    public static bool IsPressed(GameAction action)
    {
        EnsureLoaded();
        string path = paths[(int)action];
        return path.Length > 0 && buttons[(int)action].IsPressed(path);
    }

    public static bool WasPressed(GameAction action)
    {
        EnsureLoaded();
        string path = paths[(int)action];
        return path.Length > 0 && buttons[(int)action].WasPressedThisFrame(path);
    }

    public static bool WasReleased(GameAction action)
    {
        EnsureLoaded();
        string path = paths[(int)action];
        return path.Length > 0 && buttons[(int)action].WasReleasedThisFrame(path);
    }

    // ---- Names for the screen ---------------------------------------------------------

    /// <summary>Short key name for hints: "E", "Tab", "Left Click", "None".</summary>
    public static string KeyName(GameAction action)
    {
        EnsureLoaded();
        int i = (int)action;
        if (names[i] == null) names[i] = KeyName(paths[i]); // cached until the key changes
        return names[i];
    }

    public static string KeyName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "None";
        string name = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        switch (name)
        {
            case "Left Button": return "Left Click";
            case "Right Button": return "Right Click";
            case "Middle Button": return "Middle Click";
            case "Forward": return "Mouse 4";
            case "Back": return path.Contains("Mouse") ? "Mouse 5" : name;
            default: return name.Length == 1 ? name.ToUpperInvariant() : name;
        }
    }

    /// <summary>
    /// Device-independent path for a pressed control ("&lt;Keyboard&gt;/e"), so the key still
    /// works if Windows names the keyboard "Keyboard1" next time.
    /// </summary>
    public static string GenericPath(InputControl control)
    {
        if (control == null || control.device == null) return string.Empty;
        string devicePath = control.device.path;
        string rest = control.path.StartsWith(devicePath) ? control.path.Substring(devicePath.Length) : "/" + control.name;
        return "<" + control.device.layout + ">" + rest;
    }

    /// <summary>
    /// Other actions using the same key at the same time (for the Settings warning).
    /// Item keys only clash with "world" keys or keys of the same item.
    /// </summary>
    public static void FindClashes(GameAction action, System.Func<GameAction, string> pathOf, List<Def> result)
    {
        result.Clear();
        string path = pathOf(action);
        if (string.IsNullOrEmpty(path)) return;
        Def me = Get(action);
        foreach (Def other in defs)
        {
            if (other.action == action) continue;
            string otherPath = pathOf(other.action);
            if (otherPath != path) continue;
            bool clash = me.context == other.context || me.context == World || other.context == World;
            if (clash) result.Add(other);
        }
    }

    // ---- Hint text: "[E] Pick up" -> "[F] Pick up" when Interact is on F -------------

    private struct Rule
    {
        public Regex regex;
        public GameAction action;
        public string format; // {0} = key name, $1 etc. kept from the match
    }

    private static readonly Rule[] rules =
    {
        new Rule { regex = new Regex(@"\[Hold E\]"), action = GameAction.Interact, format = "[Hold {0}]" },
        new Rule { regex = new Regex(@"\[E\]"), action = GameAction.Interact, format = "[{0}]" },
        new Rule { regex = new Regex(@"\b(Hold|hold|Press|press) E\b"), action = GameAction.Interact, format = "$1 {0}" },
        new Rule { regex = new Regex(@"\bE to place\b"), action = GameAction.Interact, format = "{0} to place" },
        new Rule { regex = new Regex(@"\b(Hold|hold) H\b"), action = GameAction.Chant, format = "$1 {0}" },
        new Rule { regex = new Regex(@"\[R\]"), action = GameAction.ReloadOrDiscard, format = "[{0}]" },
        new Rule { regex = new Regex(@"\bR to discard\b"), action = GameAction.ReloadOrDiscard, format = "{0} to discard" },
        new Rule { regex = new Regex(@"\[Q\]|\(Q\)"), action = GameAction.ThrowOrDrop, format = "[{0}]" },
        new Rule { regex = new Regex(@"\bRelease Q\b"), action = GameAction.ThrowOrDrop, format = "Release {0}" },
        new Rule { regex = new Regex(@"\[(SPACE|Space)\]"), action = GameAction.FlashlightCrank, format = "[{0}]" },
        new Rule { regex = new Regex(@"\bHold Left Click to eat\b"), action = GameAction.EatSnack, format = "Hold {0} to eat" },
        new Rule { regex = new Regex(@"\bHold Right Click on a friend\b"), action = GameAction.FeedFriend, format = "Hold {0} on a friend" },
    };

    // Small cache: prompts are set every frame by some HUDs.
    private struct HintEntry { public string input, output; }
    private static readonly List<HintEntry> hintCache = new List<HintEntry>(32);

    /// <summary>
    /// Replace the default key in a hint with the player's key. Text with the
    /// default keys is returned unchanged (same string, no allocation).
    /// The H rule always applies when Chant is not on H (old texts / assets still say "hold H").
    /// </summary>
    public static string Fix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        EnsureLoaded();
        for (int i = 0; i < hintCache.Count; i++)
            if (ReferenceEquals(hintCache[i].input, text) || hintCache[i].input == text)
                return hintCache[i].output;

        string result = text;
        foreach (Rule rule in rules)
        {
            bool changed = rule.action == GameAction.Chant ? GetPath(GameAction.Chant) != "<Keyboard>/h" : !IsDefault(rule.action);
            if (!changed) continue;
            string replacement = rule.format.Replace("{0}", KeyName(rule.action).Replace("$", "$$"));
            result = rule.regex.Replace(result, replacement);
        }

        if (hintCache.Count >= 32) hintCache.RemoveAt(0);
        hintCache.Add(new HintEntry { input = text, output = result });
        return result;
    }
}
