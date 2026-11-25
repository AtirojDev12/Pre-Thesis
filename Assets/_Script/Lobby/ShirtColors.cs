using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 25 Nov 2026 (Mr.k): the 7 shirt colours (game-show pastels) and the painter that puts each
/// player's colour on their body.
///   0 White (default), 1 Yellow, 2 Sky Blue, 3 Pink, 4 Green, 5 Red, 6 Purple.
/// Mix and match: the OUTER shirt ("Shirt" material) and the INNER t-shirt ("T_Shirt" material) each
/// have their own colour. Two players never wear the same OUTER colour (RoHRoomPlayer checks it on the
/// server); the inner colour is free. Both live on the player's seat, so they stay into the match.
/// LOCAL look only: every PC paints every body from the synced seat colour.
/// </summary>
public static class ShirtColors
{
    public static readonly string[] Names = { "White", "Yellow", "Sky Blue", "Pink", "Green", "Red", "Purple" };

    public static readonly Color[] Colours =
    {
        new Color(1f, 1f, 1f),          // White (default: the model's own colour)
        new Color(1f, 0.92f, 0.55f),    // Yellow
        new Color(0.6f, 0.82f, 1f),     // Sky Blue
        new Color(1f, 0.7f, 0.84f),     // Pink
        new Color(0.62f, 0.92f, 0.68f), // Green
        new Color(1f, 0.58f, 0.58f),    // Red
        new Color(0.78f, 0.66f, 1f),    // Purple
    };

    public static int Count => Colours.Length;

    /// <summary>Offline test (no room): the local player's outer / inner colour.</summary>
    public static int OfflineColour { get; set; }
    public static int OfflineInner { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { OfflineColour = 0; OfflineInner = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartPainter()
    {
        var go = new GameObject("Shirt Colour Painter") { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ShirtPainter>();
    }

    /// <summary>The INNER t-shirt colour this PC's own player wears now.</summary>
    public static int LocalInner()
    {
        if (NetworkMode.IsOffline) return OfflineInner;
        RoHRoomPlayer seat = RoHRoomPlayer.LocalSeat;
        return seat != null ? seat.InnerColor : 0;
    }

    /// <summary>Inner t-shirt colour (repeats allowed).</summary>
    public static void RequestInner(int colour)
    {
        if (colour < 0 || colour >= Count) return;
        if (NetworkMode.IsOffline) { OfflineInner = colour; return; }
        RoHRoomPlayer seat = RoHRoomPlayer.LocalSeat;
        if (seat != null) seat.RequestInner(colour);
    }

    /// <summary>The OUTER shirt colour this PC's own player wears now.</summary>
    public static int LocalColour()
    {
        if (NetworkMode.IsOffline) return OfflineColour;
        RoHRoomPlayer seat = RoHRoomPlayer.LocalSeat;
        return seat != null ? seat.ShirtColor : 0;
    }

    /// <summary>Is this OUTER colour worn by another player?</summary>
    public static bool TakenByOther(int colour)
    {
        if (NetworkMode.IsOffline) return false;
        return RoHRoomPlayer.ShirtTaken(colour, RoHRoomPlayer.LocalSeat);
    }

    /// <summary>Ask to wear an OUTER colour (the server refuses one that is taken).</summary>
    public static void Request(int colour)
    {
        if (colour < 0 || colour >= Count) return;
        if (NetworkMode.IsOffline) { OfflineColour = colour; return; }
        RoHRoomPlayer seat = RoHRoomPlayer.LocalSeat;
        if (seat != null) seat.RequestShirt(colour);
    }

    /// <summary>Paints one body now (used by the painter; also safe to call any time).</summary>
    public static void Paint(GameObject body, int outer, int inner)
    {
        if (body == null) return;
        ShirtTint tint = body.GetComponent<ShirtTint>();
        if (tint == null) tint = body.AddComponent<ShirtTint>();
        tint.Apply(outer, inner);
    }
}

/// <summary>25 Nov: on a player body. Remembers its shirt materials and paints them.</summary>
[DisallowMultipleComponent]
public sealed class ShirtTint : MonoBehaviour
{
    private readonly List<Material> shirts = new List<Material>();
    private readonly List<Color> originals = new List<Color>();
    private readonly List<bool> isInner = new List<bool>();
    private int shownOuter = -1, shownInner = -1;
    private bool found;

    public void Apply(int outer, int inner)
    {
        if (outer == shownOuter && inner == shownInner && found) return;
        if (!found) Find();
        shownOuter = outer;
        shownInner = inner;
        Color outerTint = ShirtColors.Colours[Mathf.Clamp(outer, 0, ShirtColors.Count - 1)];
        Color innerTint = ShirtColors.Colours[Mathf.Clamp(inner, 0, ShirtColors.Count - 1)];
        for (int i = 0; i < shirts.Count; i++)
            if (shirts[i] != null) shirts[i].color = originals[i] * (isInner[i] ? innerTint : outerTint);
    }

    private void Find()
    {
        found = true;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
            Material[] shared = r.sharedMaterials;
            bool has = false;
            foreach (Material m in shared) if (IsShirt(m)) { has = true; break; }
            if (!has) continue;
            Material[] own = r.materials; // own copies: only this body changes
            foreach (Material m in own)
            {
                if (!IsShirt(m)) continue;
                shirts.Add(m);
                originals.Add(m.color);
                isInner.Add(m.name.Replace(" (Instance)", "") != "Shirt"); // T_Shirt / T-Shirt = inner
            }
        }
    }

    private static bool IsShirt(Material m)
    {
        if (m == null) return false;
        string n = m.name.Replace(" (Instance)", "");
        return n == "Shirt" || n == "T_Shirt" || n == "T-Shirt";
    }

    private void OnDestroy()
    {
        foreach (Material m in shirts) if (m != null) Destroy(m);
    }
}

/// <summary>25 Nov: paints every player's body a few times a second (bodies change between lobby and match).</summary>
public sealed class ShirtPainter : MonoBehaviour
{
    private float next;

    private void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.25f;
        if (NetworkMode.IsOffline)
        {
            if (PlayerHealth.LocalInstance != null)
                ShirtColors.Paint(PlayerHealth.LocalInstance.gameObject, ShirtColors.OfflineColour, ShirtColors.OfflineInner);
            return;
        }
        if (!NetworkClient.active) return;
        foreach (RoHRoomPlayer seat in RoHRoomPlayer.All)
        {
            if (seat == null || seat.TalkerNetId == 0) continue;
            if (NetworkClient.spawned.TryGetValue(seat.TalkerNetId, out NetworkIdentity body) && body != null && body.gameObject != seat.gameObject)
                ShirtColors.Paint(body.gameObject, seat.ShirtColor, seat.InnerColor);
        }
    }
}
