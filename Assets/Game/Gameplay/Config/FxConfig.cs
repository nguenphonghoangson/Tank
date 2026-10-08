using UnityEngine;
using UnityEngine.Audio;

namespace Tank.Gameplay.Config
{
    /// <summary>Everything that is not weapon-specific: tank hits and deaths, spawning, dash, match jingles and the music.</summary>
    [CreateAssetMenu(menuName = "Tank/Config/Fx", fileName = "FxConfig")]
    public sealed class FxConfig : ScriptableObject
    {
        [Header("Tank")]
        public GameObject tankHitVfx;
        public GameObject tankExplosionVfx;
        public GameObject tankDebrisVfx;
        public GameObject spawnVfx;
        public GameObject dashVfx;
        public SfxCue tankHitSfx;
        public SfxCue tankExplosionSfx;
        public SfxCue spawnSfx;
        public SfxCue readySfx;

        [Header("Items and cores")]
        public GameObject corePickedVfx;
        public SfxCue corePickedSfx;
        public SfxCue coreOfferedSfx;

        [Header("Match")]
        public SfxCue countdownSfx;
        public SfxCue celebrationSfx;
        public SfxCue scoreChangeSfx;

        [Header("Music")]
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = 0.35f;
        public AudioMixerGroup musicGroup;
    }
}
