using UnityEngine;
using UnityEngine.Audio;

namespace Tank.Gameplay.Config
{
    /// <summary>A sound event: a few variations of one clip, played with a little random pitch so repeats do not sound mechanical.</summary>
    [CreateAssetMenu(menuName = "Tank/Audio/Sfx Cue", fileName = "SfxCue")]
    public sealed class SfxCue : ScriptableObject
    {
        public AudioClip[] clips = new AudioClip[0];
        [Range(0f, 1f)] public float volume = 1f;
        public float pitchBase = 1f;
        public float pitchVariation = 0.1f;
        public AudioMixerGroup mixerGroup;

        public bool TryPick(out AudioClip clip, out float pitch)
        {
            clip = null; pitch = pitchBase;
            if (clips == null || clips.Length == 0) return false;
            clip = clips[Random.Range(0, clips.Length)];
            pitch = pitchBase + Random.Range(-pitchVariation, pitchVariation);
            return clip != null;
        }
    }
}
