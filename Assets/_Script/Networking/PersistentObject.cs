using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps this GameObject alive across scene loads, and makes sure only one copy
/// of it ever exists.
///
/// The reason this exists: Mirror changes scenes with a single (non-additive)
/// load, which destroys everything that is not marked DontDestroyOnLoad.
/// NetworkManager marks itself, but EOS_Manager does NOT -- EOSSDKComponent has
/// no DontDestroyOnLoad call anywhere in it. So the moment a match moves from
/// the menu into a map, the EOS manager is destroyed, EOSSDKComponent.Instance
/// goes null, and the next call to it lazily builds a brand new one with no API
/// keys assigned. That fails to initialise and throws a NullReferenceException
/// every frame -- the same cascade that was killing Play mode in TestWalk,
/// except mid-match.
///
/// Put this on EOS_Manager (and on anything else that must survive a scene
/// change, such as an audio manager or a match-state holder).
/// </summary>
[DisallowMultipleComponent]
public class PersistentObject : MonoBehaviour
{
    [Tooltip("Objects sharing this key count as the same thing: the first one survives and any later copy deletes itself. Leave empty to use this GameObject's name.")]
    [SerializeField] private string identity;

    [Tooltip("Log when this object claims persistence or destroys a duplicate. Useful while setting scene flow up.")]
    [SerializeField] private bool logLifecycle = false;

    // Runtime-only lookup, keyed so that two DIFFERENT persistent objects (the
    // EOS manager and an audio manager, say) don't delete each other. This is
    // not save data and never crosses the network, so the project's
    // "no Dictionary in serialized/network structures" rule does not apply --
    // that rule is about JsonUtility and Mirror payloads, not runtime state.
    private static readonly Dictionary<string, PersistentObject> Instances =
        new Dictionary<string, PersistentObject>();

    private string _key;

    // Statics survive between Play sessions when Unity 6's domain reload is
    // disabled, which would otherwise leave entries pointing at destroyed
    // objects and make the real one delete itself on the next run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instances.Clear();

    private void Awake()
    {
        _key = string.IsNullOrWhiteSpace(identity) ? gameObject.name : identity;

        if (Instances.TryGetValue(_key, out PersistentObject existing) && existing != null && existing != this)
        {
            // One already came through a previous scene. Keep it -- it holds the
            // live state (an initialised EOS SDK, for instance) -- and drop this
            // newcomer before anything on it can run.
            if (logLifecycle)
                Debug.Log($"[PersistentObject] '{_key}' already exists, destroying this duplicate.", this);

            Destroy(gameObject);
            return;
        }

        Instances[_key] = this;

        // DontDestroyOnLoad only works on root objects; Unity ignores the call
        // and logs a warning if the object is parented to something.
        if (transform.parent != null) transform.SetParent(null, true);

        DontDestroyOnLoad(gameObject);

        if (logLifecycle)
            Debug.Log($"[PersistentObject] '{_key}' will now survive scene loads.", this);
    }

    private void OnDestroy()
    {
        // Only clear the slot if it still points at us. A duplicate destroying
        // itself must not evict the real one.
        if (_key != null && Instances.TryGetValue(_key, out PersistentObject current) && current == this)
            Instances.Remove(_key);
    }
}
