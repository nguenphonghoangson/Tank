using System;
using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// Tank simulation: consumes a TankCommand (movement, independent turret, firing, reload, skill) and owns
    /// health, ammo and temporary buffs. It raises events for feedback/scoring and knows nothing about input
    /// devices or networking.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TankUnit : MonoBehaviour
    {
        public const int MaxSlots = 8;
        public const float AssistWindow = 5f;

        [Header("Rig")]
        public Transform turretPivot;
        public Transform muzzle;
        public Collider bodyCollider;
        public CombatFx fx;

        [Header("Identity")]
        public int team;
        public int slot = -1;
        public string displayName = "Tank";
        public bool isLocal;

        [Header("Movement (tracked: velocity follows the hull, braking is hard, no sideways drift)")]
        public float moveSpeed = 14f;
        public float acceleration = 80f;
        public float brakeDeceleration = 150f;
        public float hullTurnSpeed = 540f;
        public float turretTurnSpeed = 720f;
        public float impulseDamping = 9f;        // how fast knockback / recoil die out

        [Header("Weapon")]
        public WeaponDef weapon;

        [Header("Legacy weapon values (only used when no WeaponDef is assigned)")]
        public float fireInterval = 0.33f;
        public int projectileDamage = 25;
        public float projectileSpeed = 45f;
        public float recoilSpeed = 1.6f;

        [Header("Health")]
        public int maxHp = 100;
        public float knockbackSpeed = 3.5f;

        public TankCommand Command;

        public int Hp { get; private set; }
        public bool IsDead { get; private set; }
        public Rigidbody Body { get; private set; }
        public TankSkill Skill { get; private set; }
        public WeaponDef PrimaryWeapon { get; private set; }
        public int Ammo { get; private set; }
        public bool IsReloading => m_Reloading;
        public bool HasSpecialWeapon => weapon != null && weapon != PrimaryWeapon;
        public float ReloadProgress => m_Reloading && weapon.reloadSeconds > 0f ? 1f - Mathf.Clamp01(m_ReloadTimer / weapon.reloadSeconds) : 1f;
        public float ProjectileSpeed => weapon != null ? weapon.projectileSpeed : projectileSpeed;
        public TankUnit Killer { get; private set; }
        public TankUnit LastAttacker { get; private set; }
        public int AssistMask { get; private set; }
        public Vector3 LastDashDirection { get; private set; }

        // temporary buffs (picked up on the map)
        public int ShieldHp => Time.time < m_ShieldUntil ? m_ShieldHp : 0;
        public float ShieldTimeLeft => Mathf.Max(0f, m_ShieldUntil - Time.time);
        public float SpeedTimeLeft => Mathf.Max(0f, m_SpeedUntil - Time.time);
        public float DamageTimeLeft => Mathf.Max(0f, m_DamageUntil - Time.time);
        public float SpeedMultiplier => Time.time < m_SpeedUntil ? m_SpeedMult : 1f;
        public float DamageMultiplier => Time.time < m_DamageUntil ? m_DamageMult : 1f;

        public event Action<TankUnit> Fired;
        public event Action<TankUnit, int, Vector3, Vector3> Damaged;   // unit, hp damage (0 = fully absorbed), hit point, hit direction
        public event Action<TankUnit, int> Healed;
        public event Action<TankUnit, Color> ItemUsed;                  // pickup effect applied (colour for feedback)
        public event Action<TankUnit> WeaponChanged;
        public event Action<TankUnit> Died;
        public event Action<TankUnit> Respawned;
        public event Action<TankUnit> SkillUsed;

        internal int SplashStamp;

        readonly float[] m_LastDamageBy = new float[MaxSlots];
        float m_Cooldown, m_ReloadTimer, m_DashUntil, m_Speed, m_ShieldUntil, m_SpeedUntil, m_DamageUntil, m_SpeedMult = 1f, m_DamageMult = 1f;
        int m_ShieldHp;
        bool m_Reloading, m_WasDashing;
        Vector3 m_DashVel, m_Extra;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Skill = GetComponent<TankSkill>();
            if (weapon == null)
            {
                weapon = ScriptableObject.CreateInstance<WeaponDef>();
                weapon.damage = projectileDamage;
                weapon.fireInterval = fireInterval;
                weapon.projectileSpeed = projectileSpeed;
                weapon.recoilSpeed = recoilSpeed;
                weapon.magazineSize = 9999;
                weapon.reloadSeconds = 0.1f;
            }
            PrimaryWeapon = weapon;
            // frictionless hull: it slides along walls and cover instead of sticking to them (drive speed is set by us, not by friction)
            if (bodyCollider != null)
                bodyCollider.sharedMaterial = new PhysicsMaterial("TankHull")
                {
                    dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            Hp = maxHp;
            Ammo = weapon.magazineSize;
            ClearDamageTimes();
        }

        /// <summary>Sets the tank's primary weapon (the one it falls back to).</summary>
        public void SetWeapon(WeaponDef w)
        {
            PrimaryWeapon = w;
            weapon = w;
            Ammo = w.magazineSize;
            m_Reloading = false;
        }

        /// <summary>Pick up a special weapon: limited shots, then back to the primary weapon.</summary>
        public void GrantWeapon(WeaponDef w)
        {
            weapon = w;
            Ammo = w.magazineSize;
            m_Reloading = false;
            m_Cooldown = Mathf.Min(m_Cooldown, 0.15f);
            WeaponChanged?.Invoke(this);
        }

        void Update()
        {
            if (IsDead) return;
            AimTurret();
            if (m_Cooldown > 0f) m_Cooldown -= Time.deltaTime;

            if (m_Reloading)
            {
                m_ReloadTimer -= Time.deltaTime;
                if (m_ReloadTimer <= 0f) { m_Reloading = false; Ammo = weapon.magazineSize; }
            }
            else if (Command.Reload && !weapon.limitedAmmo && Ammo < weapon.magazineSize) StartReload();

            if (Command.Fire && m_Cooldown <= 0f && !m_Reloading)
            {
                if (Ammo > 0) Fire(); else StartReload();
            }
            if (Skill != null) Skill.Tick(this, Command.Skill);
        }

        void FixedUpdate()
        {
            if (IsDead) return;
            float dt = Time.fixedDeltaTime;
            m_Extra *= Mathf.Exp(-impulseDamping * dt);

            if (Time.time < m_DashUntil)
            {
                m_WasDashing = true;
                Body.linearVelocity = m_DashVel;
                return;
            }

            Quaternion rot = Body.rotation;
            Vector3 fwd = rot * Vector3.forward;
            if (m_WasDashing)
            {
                // leave the dash with at most normal driving speed in the hull direction
                m_WasDashing = false;
                m_Speed = Mathf.Clamp(Vector3.Dot(m_DashVel, fwd), 0f, moveSpeed);
            }

            // collisions must never leave the hull spinning: a tracked tank only turns when the player turns it
            Body.angularVelocity = Vector3.zero;

            // a head-on block (wall, another tank) stops the drive; glancing contact just slides and keeps speed
            float actual = Vector3.Dot(Body.linearVelocity - m_Extra, fwd);
            if (actual < m_Speed * 0.5f) m_Speed = Mathf.Max(0f, actual);

            Vector2 mv = Command.Move;
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            float targetSpeed = 0f;
            if (mv.sqrMagnitude > 0.0025f)
            {
                Vector3 desired = new Vector3(mv.x, 0f, mv.y);
                float targetYaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                float yaw = Mathf.MoveTowardsAngle(rot.eulerAngles.y, targetYaw, hullTurnSpeed * dt);
                rot = Quaternion.Euler(0f, yaw, 0f);
                Body.MoveRotation(rot);
                fwd = rot * Vector3.forward;
                // full speed once the hull points roughly where the stick points; a sharp turn brakes first
                // (never drives away from the stick), then accelerates hard along the new heading
                float dot = Vector3.Dot(fwd, desired.normalized);
                float align = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dot + 0.1f) / 0.75f));
                targetSpeed = moveSpeed * SpeedMultiplier * mv.magnitude * align;
            }

            float rate = targetSpeed > m_Speed ? acceleration : brakeDeceleration;
            m_Speed = Mathf.MoveTowards(m_Speed, targetSpeed, rate * dt);

            // tracked drive: all motion is along the hull; only knockback and recoil add sideways movement and they fade fast
            Vector3 vel = fwd * m_Speed + m_Extra;
            vel.y = 0f;
            Body.linearVelocity = vel;
        }

        void AimTurret()
        {
            Vector3 to = Command.AimPoint - turretPivot.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.04f) return;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float cur = turretPivot.eulerAngles.y;
            // unscaled: the turret stays responsive during hit-stop
            turretPivot.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(cur, yaw, turretTurnSpeed * Time.unscaledDeltaTime), 0f);
        }

        void StartReload()
        {
            if (m_Reloading || weapon.limitedAmmo) return;
            m_Reloading = true;
            m_ReloadTimer = weapon.reloadSeconds;
        }

        void Fire()
        {
            m_Cooldown = weapon.fireInterval;
            Ammo--;
            Vector3 aim = muzzle.forward;
            int pellets = Mathf.Max(1, weapon.pellets);
            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = aim;
                if (weapon.spreadDegrees > 0f)
                    dir = Quaternion.Euler(0f, UnityEngine.Random.Range(-weapon.spreadDegrees, weapon.spreadDegrees) * 0.5f, 0f) * aim;
                if (fx != null) fx.SpawnProjectile(this, muzzle.position, dir, weapon);
            }
            m_Extra += -aim * weapon.recoilSpeed;
            m_Extra = Vector3.ClampMagnitude(m_Extra, 5f);
            Fired?.Invoke(this);

            if (Ammo <= 0)
            {
                if (weapon.limitedAmmo)
                {
                    // special weapon is empty: back to the primary weapon with a full magazine
                    weapon = PrimaryWeapon;
                    Ammo = weapon.magazineSize;
                    WeaponChanged?.Invoke(this);
                }
                else StartReload();
            }
        }

        public void StartDash(Vector3 direction, float speed, float duration)
        {
            LastDashDirection = direction;
            m_DashVel = direction * speed;
            m_DashUntil = Time.time + duration;
            SkillUsed?.Invoke(this);
        }

        public void TakeDamage(int damage, Vector3 point, Vector3 dir, TankUnit source = null)
        {
            if (IsDead) return;
            LastAttacker = source;
            if (source != null && source != this && source.slot >= 0 && source.slot < MaxSlots) m_LastDamageBy[source.slot] = Time.time;

            int rawDamage = damage;
            int absorbed = Mathf.Min(ShieldHp, damage);
            if (absorbed > 0) { m_ShieldHp -= absorbed; damage -= absorbed; }
            Hp = Mathf.Max(0, Hp - damage);

            // knockback scales with the hit: a cannon shell shoves, a machine-gun bullet must not drag the tank along
            float shove = Mathf.Clamp(rawDamage / 25f, 0.05f, 1.2f);
            m_Extra += new Vector3(dir.x, 0f, dir.z).normalized * (knockbackSpeed * shove);
            m_Extra = Vector3.ClampMagnitude(m_Extra, 5f);
            Damaged?.Invoke(this, damage, point, dir);
            if (Hp == 0) Die(source);
        }

        /// <summary>Returns the amount actually healed.</summary>
        public int Heal(int amount)
        {
            if (IsDead) return 0;
            int applied = Mathf.Min(amount, maxHp - Hp);
            if (applied <= 0) return 0;
            Hp += applied;
            Healed?.Invoke(this, applied);
            return applied;
        }

        public void GiveShield(int amount, float seconds, Color feedback)
        {
            m_ShieldHp = amount;
            m_ShieldUntil = Time.time + seconds;
            ItemUsed?.Invoke(this, feedback);
        }

        public void GiveSpeed(float multiplier, float seconds, Color feedback)
        {
            m_SpeedMult = multiplier;
            m_SpeedUntil = Time.time + seconds;
            ItemUsed?.Invoke(this, feedback);
        }

        public void GiveDamage(float multiplier, float seconds, Color feedback)
        {
            m_DamageMult = multiplier;
            m_DamageUntil = Time.time + seconds;
            ItemUsed?.Invoke(this, feedback);
        }

        void Die(TankUnit killer)
        {
            IsDead = true;
            Killer = killer;
            AssistMask = 0;
            for (int s = 0; s < MaxSlots; s++)
            {
                if (killer != null && s == killer.slot) continue;
                if (Time.time - m_LastDamageBy[s] <= AssistWindow) AssistMask |= 1 << s;
            }
            Command = default;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            if (bodyCollider != null) bodyCollider.enabled = false;
            Died?.Invoke(this);
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            Body.isKinematic = false;
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            if (bodyCollider != null) bodyCollider.enabled = true;
            Hp = maxHp;
            weapon = PrimaryWeapon;
            Ammo = weapon.magazineSize;
            m_Reloading = false;
            m_Cooldown = 0.5f;
            m_DashUntil = 0f;
            m_WasDashing = false;
            m_Speed = 0f;
            m_Extra = Vector3.zero;
            m_ShieldUntil = m_SpeedUntil = m_DamageUntil = 0f;
            m_ShieldHp = 0;
            IsDead = false;
            Killer = null;
            LastAttacker = null;
            AssistMask = 0;
            ClearDamageTimes();
            if (Skill != null) Skill.ResetSkill();
            Command = default;
            WeaponChanged?.Invoke(this);
            Respawned?.Invoke(this);
        }

        void ClearDamageTimes()
        {
            for (int i = 0; i < MaxSlots; i++) m_LastDamageBy[i] = -999f;
        }
    }
}
