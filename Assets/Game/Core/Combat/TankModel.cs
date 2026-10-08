using Tank.Core.Cores;
using Tank.Core.Movement;
using UnityEngine;

namespace Tank.Core.Combat
{
    /// <summary>What a player (or a bot, or the network) wants this tick. Built by commands, consumed by the simulation.</summary>
    public struct TankIntent
    {
        public Vector2 Move;
        public float AimYaw;
        public bool Fire, Dash;
    }

    public readonly struct TankSettings
    {
        public readonly int MaxHp;
        public readonly float TurretTurnSpeed, DashSpeed, DashDuration, DashCooldown, HitRadius, MuzzleForward, RespawnSeconds;
        public readonly float SpeedBuffMultiplier, DamageBuffMultiplier;       // what the speed and damage items do while they last
        public TankSettings(int maxHp, float turretTurnSpeed, float dashSpeed, float dashDuration, float dashCooldown, float hitRadius, float muzzleForward, float respawnSeconds,
            float speedBuffMultiplier = 1.4f, float damageBuffMultiplier = 1.5f)
        {
            MaxHp = maxHp; TurretTurnSpeed = turretTurnSpeed; DashSpeed = dashSpeed; DashDuration = dashDuration; DashCooldown = dashCooldown;
            HitRadius = hitRadius; MuzzleForward = muzzleForward; RespawnSeconds = respawnSeconds; SpeedBuffMultiplier = speedBuffMultiplier; DamageBuffMultiplier = damageBuffMultiplier;
        }
    }

    public sealed class Health
    {
        public int Max { get; private set; }
        public int Current { get; private set; }
        public Health(int max) { Max = max; Current = max; }
        public void Restore() { Current = Max; }
        public void SetMax(int max) { Max = Mathf.Max(1, max); Current = Mathf.Min(Current, Max); }
        public void Heal(int amount) { if (Current > 0) Current = Mathf.Min(Max, Current + Mathf.Max(0, amount)); }
        /// <summary>For a client mirroring the server: take the authoritative value.</summary>
        public void Set(int value) { Current = Mathf.Clamp(value, 0, Max); }
        /// <returns>true when this damage killed it.</returns>
        public bool Damage(int amount)
        {
            if (Current <= 0 || amount <= 0) return false;
            Current = Mathf.Max(0, Current - amount);
            return Current == 0;
        }
    }

    public sealed class WeaponLoadout
    {
        public int WeaponId;       // 0 is the default weapon: unlimited ammo
        public int Ammo;
        public float Cooldown;
        public void Reset() { WeaponId = 0; Ammo = 0; Cooldown = 0f; }
    }

    public struct DashState { public float Time, Cooldown, Yaw; }

    /// <summary>
    /// Timed effects from items. SpeedTime is stepped by the movement simulation (it changes how the tank drives, so a predicting client has
    /// to step it too); the rest are stepped by the server's status system.
    /// </summary>
    public struct StatusEffects { public float SpeedTime, DamageTime, ShieldTime; public int Shield; }

    /// <summary>One tank as the rules see it. No GameObject, no networking: views and netcode wrap this, they are not part of it.</summary>
    public sealed class TankModel
    {
        public readonly int Id;
        public string Name { get; set; }
        public int ColorSlot;
        public KinematicState Body;
        public float TurretYaw;
        public readonly Health Health;
        public readonly WeaponLoadout Weapon = new WeaponLoadout();
        public DashState Dash;
        public TankIntent Intent;
        public StatusEffects Status;
        public ushort Cores;
        public CoreModifiers Mods = CoreModifiers.Identity;
        public bool IsBot;
        public float LastDamagedAt = -100f;
        readonly int m_BaseMaxHp;

        public TankModel(int id, string name, int colorSlot, int maxHp) { Id = id; Name = name; ColorSlot = colorSlot; m_BaseMaxHp = maxHp; Health = new Health(maxHp); }

        /// <summary>Replaces the tank's cores and recomputes what they do. A core that raises max health also fills the new points.</summary>
        public void SetCores(ushort mask, ICoreCatalog catalog)
        {
            int oldMax = Health.Max;
            Cores = mask;
            Mods = CoreMask.Combine(catalog, mask);
            Health.SetMax(Mathf.Max(30, m_BaseMaxHp + Mods.MaxHpBonus));
            if (Health.Max > oldMax) Health.Heal(Health.Max - oldMax);
        }
        public bool IsAlive => Health.Current > 0;
        public bool HasShield => Status.Shield > 0;
        public Vector2 Position => Body.Position;
    }
}
