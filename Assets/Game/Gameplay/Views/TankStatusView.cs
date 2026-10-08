using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>What the item buffs look like on the tank: a shield bubble, speed trails behind the tracks, and embers while its shots are boosted.</summary>
    public sealed class TankStatusView : MonoBehaviour
    {
        [SerializeField] GameObject shieldBubble;
        [SerializeField] TrailRenderer[] speedTrails = new TrailRenderer[0];
        [SerializeField] ParticleSystem damageEmbers;

        bool m_Speed, m_Damage;

        public void SetStatus(bool shield, bool speed, bool damage)
        {
            if (shieldBubble != null && shieldBubble.activeSelf != shield) shieldBubble.SetActive(shield);
            if (speed != m_Speed)
            {
                m_Speed = speed;
                foreach (TrailRenderer t in speedTrails) if (t != null) { t.emitting = speed; if (!speed) t.Clear(); }
            }
            if (damage != m_Damage)
            {
                m_Damage = damage;
                if (damageEmbers != null) { if (damage) damageEmbers.Play(); else damageEmbers.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            }
        }

        public void Hide() { SetStatus(false, false, false); }
    }
}
