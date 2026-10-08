using Unity.Cinemachine;
using UnityEngine;

namespace Tank.Gameplay.CameraSystem
{
    /// <summary>Camera shake through Cinemachine's impulse system: a source here, a listener on the Cinemachine camera.</summary>
    public sealed class CinemachineShaker : MonoBehaviour, ICameraShaker
    {
        [SerializeField] CinemachineImpulseSource source;
        [SerializeField, Range(0f, 3f)] float maxStrength = 0.5f;
        [SerializeField, Min(0f)] float minInterval = 0.15f;      // hits arrive in bursts; one shake per burst keeps the picture steady

        float m_Last = -10f;

        public void Shake(float strength)
        {
            if (source == null || strength < 0.03f || Time.unscaledTime - m_Last < minInterval) return;
            m_Last = Time.unscaledTime;
            source.GenerateImpulseWithForce(Mathf.Min(strength, maxStrength));
        }
    }
}
