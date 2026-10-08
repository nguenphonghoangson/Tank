using Tank.Gameplay.Config;
using Reflex.Attributes;
using UnityEngine;

namespace Tank.Gameplay.Fx
{
    /// <summary>Loops the match music.</summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        FxConfig m_Config;

        [Inject]
        void Construct(FxConfig config) { m_Config = config; }

        void Start()
        {
            if (m_Config == null || m_Config.music == null) return;
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.clip = m_Config.music; src.loop = true; src.volume = m_Config.musicVolume;
            src.spatialBlend = 0f; src.outputAudioMixerGroup = m_Config.musicGroup;
            src.Play();
        }
    }
}
