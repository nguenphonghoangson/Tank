using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Angled top-down follow camera with aim look-ahead, trauma-based shake and a spring kick on firing.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 22f, -13f);
        public float follow = 16f;
        public float aimLookahead = 0.18f;
        public float maxLookahead = 6f;

        [Header("Shake")]
        public float maxShakeOffset = 0.8f;
        public float maxShakeRoll = 2.5f;
        public float traumaDecay = 1.8f;

        Quaternion m_BaseRotation;
        Vector3 m_Pos, m_Kick, m_KickVel, m_Aim, m_Look;
        float m_Trauma;
        bool m_HasAim, m_Init;

        public void AddTrauma(float amount) { m_Trauma = Mathf.Min(1f, m_Trauma + amount); }
        public void Kick(Vector3 worldDirection, float amount) { m_Kick += worldDirection * amount; }
        public void SetAimPoint(Vector3 point) { m_Aim = point; m_HasAim = true; }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;   // shake keeps playing through hit-stop

            Vector3 focus = target.position;
            if (m_HasAim)
            {
                Vector3 d = m_Aim - focus;
                d.y = 0f;
                // smoothed so mouse jitter does not shake the whole view
                m_Look = Vector3.Lerp(m_Look, Vector3.ClampMagnitude(d * aimLookahead, maxLookahead), 1f - Mathf.Exp(-9f * dt));
                focus += m_Look;
            }
            Vector3 desired = focus + offset;
            if (!m_Init)
            {
                m_Init = true;
                m_Pos = desired;
                m_BaseRotation = Quaternion.LookRotation(-offset.normalized);
            }
            m_Pos = Vector3.Lerp(m_Pos, desired, 1f - Mathf.Exp(-follow * dt));

            // critically damped spring pulls the kick back to zero
            m_KickVel += (-m_Kick * 320f - m_KickVel * 36f) * dt;
            m_Kick += m_KickVel * dt;

            float s = m_Trauma * m_Trauma;
            float t = Time.unscaledTime * 38f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, 0f, Mathf.PerlinNoise(0f, t + 31f) - 0.5f) * (2f * maxShakeOffset * s);
            float roll = (Mathf.PerlinNoise(t + 71f, 9f) - 0.5f) * 2f * maxShakeRoll * s;

            transform.position = m_Pos + m_Kick + shake;
            transform.rotation = m_BaseRotation * Quaternion.Euler(0f, 0f, roll);
            m_Trauma = Mathf.Max(0f, m_Trauma - traumaDecay * dt);
        }
    }
}
