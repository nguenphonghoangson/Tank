using System.Collections.Generic;
using Tank.Gameplay.Config;
using UnityEngine;

namespace Tank.Gameplay.Fx
{
    /// <summary>What the presentation layer can ask for. Gameplay code depends on this, never on AudioSource or particle pooling.</summary>
    public interface IFxService
    {
        void PlayVfx(GameObject prefab, Vector3 position, Quaternion rotation);
        void PlaySfx(SfxCue cue, Vector3 position);
        void PlaySfx2D(SfxCue cue);
    }

    /// <summary>Pooled particle effects plus a ring of audio sources. A scene object so it owns real GameObjects, but nothing else knows that.</summary>
    public sealed class FxService : MonoBehaviour, IFxService
    {
        [SerializeField] int audioVoices = 24;
        [SerializeField] float spatialBlend = 0.55f;
        [SerializeField] float minDistance = 12f;
        [SerializeField] float maxDistance = 90f;

        struct Live { public GameObject go; public float endTime; }

        [Header("Budget")]
        [SerializeField, Min(1)] int maxLiveEffects = 48;
        [SerializeField, Min(1)] int maxLiveEffectsMobile = 16;
        [SerializeField, Min(0f)] float minIntervalSeconds = 0.04f;
        [SerializeField, Range(0.2f, 1f)] float mobileParticleScale = 0.5f;

        readonly Dictionary<GameObject, float> m_LastPlayed = new Dictionary<GameObject, float>();
        readonly HashSet<GameObject> m_Tuned = new HashSet<GameObject>();
        int m_MaxLive; float m_MinInterval, m_ParticleScale = 1f;
        ObjectPool m_Pool;
        readonly List<Live> m_Live = new List<Live>(64);
        AudioSource[] m_Voices;
        int m_NextVoice;

        void Awake()
        {
            m_Pool = new ObjectPool(transform);
            bool mobile = Application.isMobilePlatform;
            m_MaxLive = mobile ? maxLiveEffectsMobile : maxLiveEffects;
            m_MinInterval = minIntervalSeconds * (mobile ? 2f : 1f);
            m_ParticleScale = mobile ? mobileParticleScale : 1f;
            m_Voices = new AudioSource[Mathf.Max(4, audioVoices)];
            for (int i = 0; i < m_Voices.Length; i++)
            {
                var go = new GameObject("Voice_" + i);
                go.transform.SetParent(transform, false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = minDistance; src.maxDistance = maxDistance;
                m_Voices[i] = src;
            }
        }

        public void PlayVfx(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;

            // budget: the same effect twice within a few frames adds particles nobody can tell apart (a minigun fires 11 times a second)
            if (m_LastPlayed.TryGetValue(prefab, out float last) && Time.time - last < m_MinInterval) return;
            m_LastPlayed[prefab] = Time.time;

            // and only so many effects at once: when the screen is full the oldest one gives way
            while (m_Live.Count >= m_MaxLive) { m_Pool.Release(m_Live[0].go); m_Live.RemoveAt(0); }

            GameObject go = m_Pool.Get(prefab, position, rotation);
            if (go == null) return;
            bool first = m_Tuned.Add(go);
            float life = 0.5f;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (first && m_ParticleScale < 1f)                       // phones: fewer particles per effect, tuned once per pooled instance
                {
                    main.maxParticles = Mathf.Max(24, Mathf.RoundToInt(main.maxParticles * m_ParticleScale));
                    var em = ps.emission; em.rateOverTimeMultiplier *= m_ParticleScale;
                }
                ps.Clear(true); ps.Play(true);
                float l = main.duration + (main.startLifetime.mode == ParticleSystemCurveMode.Constant ? main.startLifetime.constant : main.startLifetime.constantMax);
                life = Mathf.Max(life, main.loop ? main.duration : l);
            }
            m_Live.Add(new Live { go = go, endTime = Time.time + Mathf.Min(life, 8f) });
        }

        public void PlaySfx(SfxCue cue, Vector3 position) { Play(cue, position, spatialBlend); }
        public void PlaySfx2D(SfxCue cue) { Play(cue, Vector3.zero, 0f); }

        void Play(SfxCue cue, Vector3 position, float blend)
        {
            if (cue == null || !cue.TryPick(out AudioClip clip, out float pitch)) return;
            AudioSource src = m_Voices[m_NextVoice]; m_NextVoice = (m_NextVoice + 1) % m_Voices.Length;     // oldest voice is recycled when all are busy
            src.transform.position = position;
            src.spatialBlend = blend; src.clip = clip; src.pitch = pitch; src.volume = cue.volume; src.outputAudioMixerGroup = cue.mixerGroup;
            src.Play();
        }

        void Update()
        {
            for (int i = m_Live.Count - 1; i >= 0; i--)
                if (Time.time >= m_Live[i].endTime) { m_Pool.Release(m_Live[i].go); m_Live.RemoveAt(i); }
        }
    }
}
