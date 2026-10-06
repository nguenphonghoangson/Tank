using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>
    /// Deterministic tank movement and turret, with the same feel as the game's TankUnit (14 m/s, hard braking, no sideways
    /// drift). A pure function of (state, command), so the client can predict and replay it and the server can run it.
    /// </summary>
    public static class SpikeSim
    {
        public const int TickRate = 30;
        public const float Dt = 1f / TickRate;
        public const float MoveSpeed = 14f, Accel = 80f, Brake = 150f, HullTurn = 540f, TurretTurn = 720f;
        public const float FireInterval = 0.33f, ShellSpeed = 55f, Range = 70f, MuzzleHeight = 1.25f, MuzzleForward = 2.5f;
        public const int Damage = 25, MaxHp = 100;
        public const float DashSpeed = 36f, DashDuration = 0.22f, DashCooldown = 4f;
        public const float SpeedMult = 1.4f, SpeedSeconds = 8f, DamageMult = 1.5f, DamageSeconds = 10f, ShieldSeconds = 12f;
        public const int HealAmount = 40, ShieldAmount = 50;
        public const float BoxHalfX = 1.05f, BoxHalfZ = 1.65f;

        public static InputCmd MakeCmd(uint seq, Vector2 move, float aimYawDeg, bool fire, uint viewTick, bool dash = false)
        {
            move = Vector2.ClampMagnitude(move, 1f);
            float a = Mathf.Repeat(aimYawDeg, 360f);
            return new InputCmd
            {
                seq = seq,
                moveX = (sbyte)Mathf.RoundToInt(move.x * 127f),
                moveZ = (sbyte)Mathf.RoundToInt(move.y * 127f),
                aim = (ushort)Mathf.Clamp(Mathf.RoundToInt(a / 360f * 65536f), 0, 65535),
                buttons = (byte)((fire ? 1 : 0) | (dash ? 2 : 0)),
                viewTick = viewTick,
            };
        }

        public static float AimYaw(InputCmd c) { return c.aim * (360f / 65536f); }
        public static bool Fire(InputCmd c) { return (c.buttons & 1) != 0; }
        public static bool Dash(InputCmd c) { return (c.buttons & 2) != 0; }

        /// <summary>Advances one tick. Returns true when this tick fires a shot (cooldown included, identical on server and client).</summary>
        public static bool Step(ref TankState s, InputCmd c)
        {
            Vector2 mv = new Vector2(c.moveX / 127f, c.moveZ / 127f);
            if (mv.sqrMagnitude > 1f) mv.Normalize();

            s.dashCd = Mathf.Max(0f, s.dashCd - Dt);
            if (Dash(c) && s.dashCd <= 0f && s.dashTime <= 0f)
            {
                s.dashYaw = mv.sqrMagnitude > 0.0025f ? Mathf.Atan2(mv.x, mv.y) * Mathf.Rad2Deg : s.yaw;
                s.dashTime = DashDuration;
                s.dashCd = DashCooldown;
            }
            float topSpeed = s.speedTime > 0f ? MoveSpeed * SpeedMult : MoveSpeed;
            s.speedTime = Mathf.Max(0f, s.speedTime - Dt);

            if (s.dashTime > 0f)
            {
                Vector3 dd = Quaternion.Euler(0f, s.dashYaw, 0f) * Vector3.forward;
                s.pos += dd * (DashSpeed * Dt);
                SpikeWorld.Resolve(ref s.pos, s.yaw);
                s.dashTime = Mathf.Max(0f, s.dashTime - Dt);
                s.speed = Mathf.Min(s.speed, topSpeed);
            }
            else
            {
                Vector3 fwd = Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward;
                float targetSpeed = 0f;
                if (mv.sqrMagnitude > 0.0025f)
                {
                    Vector3 desired = new Vector3(mv.x, 0f, mv.y);
                    float targetYaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                    s.yaw = Mathf.MoveTowardsAngle(s.yaw, targetYaw, HullTurn * Dt);
                    fwd = Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward;
                    float dot = Vector3.Dot(fwd, desired.normalized);
                    float align = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dot + 0.1f) / 0.75f));
                    targetSpeed = topSpeed * mv.magnitude * align;
                }
                float rate = targetSpeed > s.speed ? Accel : Brake;
                s.speed = Mathf.MoveTowards(s.speed, targetSpeed, rate * Dt);

                Vector3 before = s.pos;
                s.pos += fwd * (s.speed * Dt);
                SpikeWorld.Resolve(ref s.pos, s.yaw);
                // a head-on block stops the drive; glancing contact keeps speed
                float moved = Vector3.Dot(s.pos - before, fwd) / Dt;
                if (moved < s.speed * 0.5f) { s.speed = Mathf.Max(0f, moved); SpikeMetrics.DbgStop++; }
            }
            SpikeMetrics.DbgPos = s.pos;

            s.turretYaw = Mathf.MoveTowardsAngle(s.turretYaw, AimYaw(c), TurretTurn * Dt);

            s.fireCd = Mathf.Max(0f, s.fireCd - Dt);
            if (Fire(c) && s.fireCd <= 0f) { s.fireCd = FireInterval; return true; }
            return false;
        }

        public static Vector3 MuzzleOrigin(TankState s)
        {
            Vector3 d = Quaternion.Euler(0f, s.turretYaw, 0f) * Vector3.forward;
            return s.pos + Vector3.up * MuzzleHeight + d * MuzzleForward;
        }

        public static Vector3 TurretDir(TankState s) { return Quaternion.Euler(0f, s.turretYaw, 0f) * Vector3.forward; }

        /// <summary>Horizontal ray against the tank's footprint (an oriented rectangle). Returns the entry distance.</summary>
        public static bool RayHitsTank(Vector3 origin, Vector3 dir, TankState t, out float dist)
        {
            dist = 0f;
            Quaternion inv = Quaternion.Euler(0f, -t.yaw, 0f);
            Vector3 o = inv * (origin - t.pos), d = inv * dir;
            float tMin = 0f, tMax = Range;
            float[] oo = { o.x, o.z }, dd = { d.x, d.z }, half = { BoxHalfX, BoxHalfZ };
            for (int i = 0; i < 2; i++)
            {
                if (Mathf.Abs(dd[i]) < 1e-6f) { if (oo[i] < -half[i] || oo[i] > half[i]) return false; continue; }
                float t1 = (-half[i] - oo[i]) / dd[i], t2 = (half[i] - oo[i]) / dd[i];
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1); tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            dist = tMin;
            return true;
        }
    }
}
