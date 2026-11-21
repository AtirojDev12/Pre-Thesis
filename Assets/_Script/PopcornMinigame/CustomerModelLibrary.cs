using UnityEngine;

/// <summary>
/// The customer bodies (NPC models + their Idle/Walk animator controllers):
/// human customers pick from Human Models, ghost customers from Ghost Models
/// (the GhostNPC variants: same rigs, ghost textures).
/// Lives at Resources/CustomerModels.asset so runtime-created customers can load it.
/// A list left empty falls back to the old tinted capsule for that customer type.
/// </summary>
[CreateAssetMenu(fileName = "CustomerModels", menuName = "Pre-Thesis/Customer Model Library")]
public sealed class CustomerModelLibrary : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("The NPC FBX (Rig: Humanoid).")]
        public GameObject model;
        [Tooltip("Controller with Idle (default) and Walk states and a bool 'IsWalking'.")]
        public RuntimeAnimatorController controller;
        [Tooltip("Keep the neck and head in the model's own rest pose (body still animates). " +
                 "Use for a model whose Mixamo rig has a flipped neck/head and droops when animated.")]
        public bool lockHeadToRestPose;
        [Tooltip("Swings both upper arms away from the body by this many degrees after the animation, " +
                 "so hands/arms don't sink into the torso. 0 = untouched.")]
        [Range(0f, 30f)] public float armSpreadDegrees;
    }

    [SerializeField] private Entry[] humanModels = new Entry[0];
    [Tooltip("Ghost customers (popcorn counter + ticket booth). Empty = ghosts stay capsules.")]
    [SerializeField] private Entry[] ghostModels = new Entry[0];

    private static CustomerModelLibrary instance;
    private static bool loaded;

    public static CustomerModelLibrary Instance
    {
        get
        {
            if (!loaded)
            {
                instance = Resources.Load<CustomerModelLibrary>("CustomerModels");
                loaded = true;
                if (instance == null)
                    Debug.LogWarning("[Customers] Missing Resources/CustomerModels asset; human customers fall back to capsules.");
            }
            return instance;
        }
    }

    /// <summary>
    /// Deterministic pick: every client passes the same synced id (ticket round /
    /// popcorn order id), so all players see the same model for the same customer.
    /// </summary>
    public bool TryPick(uint seed, out Entry entry) => TryPick(seed, false, out entry);

    /// <summary>Same deterministic pick, from the ghost list when <paramref name="ghost"/> is true.</summary>
    public bool TryPick(uint seed, bool ghost, out Entry entry)
    {
        entry = default;
        Entry[] models = ghost ? ghostModels : humanModels;
        if (models == null) return false;
        int valid = 0;
        foreach (Entry e in models) if (e.model != null) valid++;
        if (valid == 0) return false;
        uint hash = seed * 2654435761u;
        int index = (int)((hash >> 16) % (uint)valid);
        foreach (Entry e in models)
        {
            if (e.model == null) continue;
            if (index-- == 0) { entry = e; return true; }
        }
        return false;
    }
}
