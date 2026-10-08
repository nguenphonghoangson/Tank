using System.Collections.Generic;
using Tank.Core.Common;
using Tank.Core.Events;
using UnityEngine;

namespace Tank.Core.Combat
{
    /// <summary>The one place damage is applied. Everything that hurts a tank (shells, splash, later hazards) goes through it.</summary>
    public interface ICombatService
    {
        void ApplyDamage(int victimId, int attackerId, int amount);
    }

    public sealed class CombatService : ICombatService
    {
        public const float AssistWindowSeconds = 10f;      // damage this recently before a kill counts as an assist

        readonly ITankRegistry m_Tanks;
        readonly IEventBus m_Bus;
        readonly IClock m_Clock;
        readonly Dictionary<int, Dictionary<int, float>> m_Damagers = new Dictionary<int, Dictionary<int, float>>();   // victim -> attacker -> last hit time
        readonly List<int> m_Scratch = new List<int>();

        public CombatService(ITankRegistry tanks, IEventBus bus, IClock clock) { m_Tanks = tanks; m_Bus = bus; m_Clock = clock; }

        public void ApplyDamage(int victimId, int attackerId, int amount)
        {
            TankModel victim = m_Tanks.Find(victimId);
            if (victim == null || !victim.IsAlive || amount <= 0) return;

            amount = AbsorbWithShield(victim, amount);
            int before = victim.Health.Current;
            bool killed = victim.Health.Damage(amount);
            victim.LastDamagedAt = m_Clock.Now;
            if (attackerId != victimId) { Record(victimId, attackerId); Lifesteal(attackerId, before - victim.Health.Current); }
            m_Bus.Publish(new TankDamaged(victimId, attackerId, amount, victim.Health.Current));
            if (!killed) return;

            m_Scratch.Clear();
            if (m_Damagers.TryGetValue(victimId, out var hits))
            {
                foreach (var kv in hits)
                    if (kv.Key != attackerId && kv.Key != victimId && m_Clock.Now - kv.Value <= AssistWindowSeconds) m_Scratch.Add(kv.Key);
                hits.Clear();
            }
            m_Bus.Publish(new TankKilled(victimId, attackerId, m_Scratch.ToArray()));
        }

        /// <summary>A shield takes the damage first; what is left goes through.</summary>
        static int AbsorbWithShield(TankModel victim, int amount)
        {
            if (victim.Status.Shield <= 0) return amount;
            int absorbed = Mathf.Min(victim.Status.Shield, amount);
            victim.Status.Shield -= absorbed;
            if (victim.Status.Shield <= 0) victim.Status.ShieldTime = 0f;
            return amount - absorbed;
        }

        void Lifesteal(int attackerId, int dealt)
        {
            TankModel a = m_Tanks.Find(attackerId);
            if (a != null && a.IsAlive && a.Mods.Lifesteal > 0f && dealt > 0) a.Health.Heal(Mathf.RoundToInt(dealt * a.Mods.Lifesteal));
        }

        void Record(int victimId, int attackerId)
        {
            if (!m_Damagers.TryGetValue(victimId, out var hits)) m_Damagers[victimId] = hits = new Dictionary<int, float>();
            hits[attackerId] = m_Clock.Now;
        }
    }
}
