using System;
using System.Collections.Generic;
using PhoenixForgeGames.Audio.Objects;
using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// The game's sounds by name ("step", "hit", "music.town"...), each a set of clips played at random. Made from
    /// Assets/UOClient/Audio/Sounds.json by Tools/Game/MakeAudio.eval.cs and loaded from Resources/Audio/SoundBank.
    /// A name with no clips is silent, so the game can name sounds before a pack provides them.
    /// </summary>
    public sealed class SoundBank : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public List<ForgeAudioClip> Clips = new List<ForgeAudioClip>();
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, Entry> _byKey;
        private readonly Dictionary<string, ForgeAudioClip> _last = new Dictionary<string, ForgeAudioClip>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A random clip for this sound (never the one this sound played last, when there's a choice), or null.</summary>
        public ForgeAudioClip Pick(string key)
        {
            Index();
            if (!_byKey.TryGetValue(key, out Entry entry) || entry.Clips.Count == 0)
            {
                return null;
            }

            ForgeAudioClip clip = entry.Clips[UnityEngine.Random.Range(0, entry.Clips.Count)];
            if (entry.Clips.Count > 1 && _last.TryGetValue(key, out ForgeAudioClip last) && clip == last)
            {
                clip = entry.Clips[(entry.Clips.IndexOf(clip) + 1) % entry.Clips.Count];
            }

            _last[key] = clip;
            return clip != null && clip.audioClip != null ? clip : null;
        }

        /// <summary>Does this sound have any clips? (Doesn't count as playing it.)</summary>
        public bool Has(string key)
        {
            Index();
            return _byKey.TryGetValue(key, out Entry e) && e.Clips.Count > 0;
        }

        private void Index()
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (Entry e in Entries)
                {
                    if (!string.IsNullOrEmpty(e.Key))
                    {
                        _byKey[e.Key] = e;
                    }
                }
            }
        }
    }
}
