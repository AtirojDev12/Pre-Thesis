using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Audio/Sound Library")]
public sealed class SoundLibrary : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public AudioClip[] clips;
        public SoundCategory category = SoundCategory.Sfx;
        [Range(0, 1)] public float volume = 0.8f;
        public bool spatial;
        [Min(0.1f)] public float minDistance = 1;
        [Min(0.2f)] public float maxDistance = 15;
        [Min(0)] public float cooldown;
    }

    [SerializeField] private Entry[] sounds = Array.Empty<Entry>();
    public Entry Find(string id)
    {
        foreach (Entry entry in sounds)
            if (entry != null && entry.id == id) return entry;
        return null;
    }
}
