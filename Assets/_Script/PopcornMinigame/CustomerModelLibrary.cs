using UnityEngine;

/// <summary>
/// The human customer bodies (NPC models + their Idle/Walk animator controllers).
/// Lives at Resources/CustomerModels.asset so runtime-created customers can load it.
/// Ghost customers do NOT use this: they stay as tinted capsules for now.
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
    public bool TryPick(uint seed, out Entry entry)
    {
        entry = default;
        if (humanModels == null) return false;
        int valid = 0;
        foreach (Entry e in humanModels) if (e.model != null) valid++;
        if (valid == 0) return false;
        uint hash = seed * 2654435761u;
        int index = (int)((hash >> 16) % (uint)valid);
        foreach (Entry e in humanModels)
        {
            if (e.model == null) continue;
            if (index-- == 0) { entry = e; return true; }
        }
        return false;
    }
}
