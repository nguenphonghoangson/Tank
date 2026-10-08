using System.Collections.Generic;
using Tank.Core.Common;
using Tank.Core.Events;
using Tank.Core.Hit;
using UnityEngine;

namespace Tank.Core.Combat
{
    public sealed class Projectile
    {
        public int Id, OwnerId, WeaponId, Damage, SplashDamage;
        public Vector2 Position, Direction;
        public float Speed, Remaining, SplashRadius;
    }

    /// <summary>What the shooter's buffs and cores add to one shot.</summary>
    public readonly struct ShotModifiers
    {
        public readonly float DamageMultiplier, SplashBonus;
        public ShotModifiers(float damageMultiplier, float splashBonus) { DamageMultiplier = damageMultiplier; SplashBonus = splashBonus; }
        public static ShotModifiers None => new ShotModifiers(1f, 0f);
    }

    /// <summary>Weapons create shots through this; they do not know how projectiles fly.</summary>
    public interface IProjectileSpawner
    {
        void Spawn(int ownerId, WeaponSpec spec, Vector2 origin, Vector2 direction, ShotModifiers modifiers);
    }

    /// <summary>Read side for views: what is in the air right now.</summary>
    public interface IProjectileQuery { IReadOnlyList<Projectile> Active { get; } }

    /// <summary>Flies every projectile each tick against the hit world and the tank hitboxes, and reports damage to the combat service.</summary>
    public sealed class ProjectileSystem : IProjectileSpawner, IProjectileQuery, ITickable
    {
        readonly IHitWorld m_World;
        readonly ITankRegistry m_Tanks;
        readonly ICombatService m_Combat;
        readonly IEventBus m_Bus;
        readonly float m_TankHitRadius;
        readonly List<Projectile> m_Active = new List<Projectile>();
        int m_NextId;

        public ProjectileSystem(IHitWorld world, ITankRegistry tanks, ICombatService combat, IEventBus bus, TankSettings tank)
        {
            m_World = world; m_Tanks = tanks; m_Combat = combat; m_Bus = bus; m_TankHitRadius = tank.HitRadius;
        }

        public IReadOnlyList<Projectile> Active => m_Active;

        public void Spawn(int ownerId, WeaponSpec spec, Vector2 origin, Vector2 direction, ShotModifiers modifiers)
        {
            int damage = Mathf.RoundToInt(spec.Damage * modifiers.DamageMultiplier);
            float splashRadius = spec.SplashRadius > 0f ? spec.SplashRadius + modifiers.SplashBonus : modifiers.SplashBonus;     // a splash core gives any weapon a burst
            float splashFactor = spec.SplashRadius > 0f ? spec.SplashFactor : 0.6f;
            var p = new Projectile
            {
                Id = ++m_NextId, OwnerId = ownerId, WeaponId = spec.Id, Damage = damage,
                SplashDamage = Mathf.RoundToInt(damage * splashFactor), SplashRadius = splashRadius,
                Position = origin, Direction = direction.normalized, Speed = spec.ProjectileSpeed, Remaining = spec.Range,
            };
            m_Active.Add(p);
            m_Bus.Publish(new ProjectileFired(p.Id, ownerId, spec.Id, origin, p.Direction));
        }

        public void Clear()
        {
            for (int i = m_Active.Count - 1; i >= 0; i--) { Projectile p = m_Active[i]; m_Bus.Publish(new ProjectileImpact(p.Id, p.WeaponId, p.Position, false)); }
            m_Active.Clear();
        }

        public void Tick(float dt)
        {
            for (int i = m_Active.Count - 1; i >= 0; i--)
            {
                Projectile p = m_Active[i];
                float step = Mathf.Min(p.Speed * dt, p.Remaining);
                float wall = m_World.Raycast(p.Position, p.Direction, step);
                TankModel hitTank = null; float tankDist = step;
                for (int k = 0; k < m_Tanks.All.Count; k++)
                {
                    TankModel t = m_Tanks.All[k];
                    if (t.Id == p.OwnerId || !t.IsAlive) continue;
                    if (CircleRay.Hit(p.Position, p.Direction, t.Position, m_TankHitRadius, tankDist, out float d) && d <= tankDist) { tankDist = d; hitTank = t; }
                }

                bool hitWall = wall < step - 1e-4f && (hitTank == null || wall <= tankDist);
                if (hitTank != null && !hitWall)
                {
                    Vector2 point = p.Position + p.Direction * tankDist;
                    m_Combat.ApplyDamage(hitTank.Id, p.OwnerId, p.Damage);
                    Explode(p, point, hitTank.Id, true);
                    m_Active.RemoveAt(i);
                    continue;
                }
                if (hitWall)
                {
                    Explode(p, p.Position + p.Direction * wall, -1, false);
                    m_Active.RemoveAt(i);
                    continue;
                }

                p.Position += p.Direction * step;
                p.Remaining -= step;
                if (p.Remaining <= 1e-4f) { Explode(p, p.Position, -1, false); m_Active.RemoveAt(i); }
            }
        }

        void Explode(Projectile p, Vector2 point, int directHitId, bool hitTank)
        {
            if (p.SplashRadius > 0f)
                for (int k = 0; k < m_Tanks.All.Count; k++)
                {
                    TankModel t = m_Tanks.All[k];
                    if (t.Id == p.OwnerId || t.Id == directHitId || !t.IsAlive) continue;
                    if ((t.Position - point).magnitude <= p.SplashRadius) m_Combat.ApplyDamage(t.Id, p.OwnerId, p.SplashDamage);
                }
            m_Bus.Publish(new ProjectileImpact(p.Id, p.WeaponId, point, hitTank));
        }
    }
}
