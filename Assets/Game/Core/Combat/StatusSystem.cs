using Tank.Core.Common;
using UnityEngine;

namespace Tank.Core.Combat
{
    /// <summary>Server-side upkeep of a tank's timed effects: the damage buff and shield run out, and Repair Nanites heal when the tank has not been hit for a while.</summary>
    public sealed class StatusSystem : ITickable
    {
        const float RegenDelaySeconds = 4f;

        readonly ITankRegistry m_Tanks;
        readonly IClock m_Clock;
        readonly System.Collections.Generic.Dictionary<int, float> m_RegenCarry = new System.Collections.Generic.Dictionary<int, float>();

        public StatusSystem(ITankRegistry tanks, IClock clock) { m_Tanks = tanks; m_Clock = clock; }

        public void Tick(float dt)
        {
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel t = m_Tanks.All[i];
                if (!t.IsAlive) continue;
                StatusEffects s = t.Status;
                s.DamageTime = Mathf.Max(0f, s.DamageTime - dt);
                if (s.ShieldTime > 0f) { s.ShieldTime -= dt; if (s.ShieldTime <= 0f) { s.ShieldTime = 0f; s.Shield = 0; } }
                t.Status = s;

                if (t.Mods.RegenPerSecond > 0f && t.Health.Current < t.Health.Max && m_Clock.Now - t.LastDamagedAt > RegenDelaySeconds)
                {
                    m_RegenCarry.TryGetValue(t.Id, out float carry);
                    carry += t.Mods.RegenPerSecond * dt;
                    int whole = (int)carry;
                    if (whole > 0) { t.Health.Heal(whole); carry -= whole; }
                    m_RegenCarry[t.Id] = carry;
                }
            }
        }
    }
}
