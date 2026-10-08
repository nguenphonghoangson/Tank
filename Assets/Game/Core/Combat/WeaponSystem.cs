using Tank.Core.Common;
using UnityEngine;

namespace Tank.Core.Combat
{
    /// <summary>Turns a tank's "fire" intent into shots: cooldown, ammo, pellet spread. How the shots fly is the projectile system's business.</summary>
    public sealed class WeaponSystem
    {
        readonly IWeaponCatalog m_Catalog;
        readonly IProjectileSpawner m_Projectiles;
        readonly IRandom m_Random;
        readonly float m_MuzzleForward, m_DamageBuff;

        public WeaponSystem(IWeaponCatalog catalog, IProjectileSpawner projectiles, IRandom random, TankSettings tank)
        {
            m_Catalog = catalog; m_Projectiles = projectiles; m_Random = random; m_MuzzleForward = tank.MuzzleForward; m_DamageBuff = tank.DamageBuffMultiplier;
        }

        public void Step(TankModel t, float dt)
        {
            WeaponLoadout w = t.Weapon;
            w.Cooldown = Mathf.Max(0f, w.Cooldown - dt);
            if (!t.IsAlive || !t.Intent.Fire || w.Cooldown > 0f) return;

            WeaponSpec spec = m_Catalog.Get(w.WeaponId);
            var mods = new ShotModifiers((t.Status.DamageTime > 0f ? m_DamageBuff : 1f) * t.Mods.DamageMult, t.Mods.SplashBonus);
            float aim = t.TurretYaw;
            for (int i = 0; i < spec.Pellets; i++)
            {
                float offset = spec.Pellets > 1
                    ? ((float)i / (spec.Pellets - 1) - 0.5f) * spec.SpreadDegrees + m_Random.Range(-1f, 1f)
                    : (spec.SpreadDegrees > 0f ? m_Random.Range(-0.5f, 0.5f) * spec.SpreadDegrees : 0f);
                float yaw = (aim + offset) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                m_Projectiles.Spawn(t.Id, spec, t.Position + dir * m_MuzzleForward, dir, mods);
            }
            w.Cooldown = spec.Interval * t.Mods.FireIntervalMult;
            if (spec.Ammo > 0 && --w.Ammo <= 0) w.Reset();       // a special weapon runs dry: back to the default one
        }

        /// <summary>Switches to a weapon with its full ammo (item pickups).</summary>
        public void Equip(TankModel t, int weaponId)
        {
            WeaponSpec spec = m_Catalog.Get(weaponId);
            t.Weapon.WeaponId = weaponId; t.Weapon.Ammo = Mathf.RoundToInt(spec.Ammo * t.Mods.AmmoMult); t.Weapon.Cooldown = Mathf.Min(t.Weapon.Cooldown, 0.15f);
        }
    }
}
