using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>A pooled effect: restarts its particle systems on spawn, fades an optional flash light, returns itself when done.</summary>
    public sealed class PooledFx : MonoBehaviour
    {
        public float duration = 1f;
        public Light flashLight;
        public float flashDuration = 0.08f;

        ParticleSystem[] m_Systems;
        Pool<PooledFx> m_Pool;
        float m_Timer, m_LightPeak;

        void Awake()
        {
            m_Systems = GetComponentsInChildren<ParticleSystem>(true);
            if (flashLight != null) m_LightPeak = flashLight.intensity;
        }

        public void Begin(Pool<PooledFx> pool, Vector3 position, Quaternion rotation)
        {
            m_Pool = pool;
            m_Timer = 0f;
            transform.SetPositionAndRotation(position, rotation);
            for (int i = 0; i < m_Systems.Length; i++)
            {
                m_Systems[i].Clear(true);
                m_Systems[i].Play(false);
            }
            if (flashLight != null)
            {
                flashLight.intensity = m_LightPeak;
                flashLight.enabled = true;
            }
        }

        void Update()
        {
            m_Timer += Time.deltaTime;
            if (flashLight != null && flashLight.enabled)
            {
                float k = 1f - m_Timer / flashDuration;
                if (k <= 0f) flashLight.enabled = false;
                else flashLight.intensity = m_LightPeak * k;
            }
            if (m_Timer >= duration) m_Pool.Release(this);
        }
    }
}
