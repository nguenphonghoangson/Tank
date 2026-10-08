using Tank.Core.Combat;
using UnityEngine;

namespace Tank.Core.Netcode
{
    /// <summary>
    /// One tick of player intent as it travels: quantised, so the client predicts with exactly the numbers the server will receive.
    /// The client applies ToIntent() to its own tank, never the raw input.
    /// </summary>
    public struct InputFrame
    {
        public uint Seq;
        public sbyte MoveX, MoveZ;      // -127..127
        public ushort Aim;              // 0..65535 = 0..360 degrees
        public byte Buttons;            // bit 0 fire, bit 1 dash

        public const byte FireBit = 1, DashBit = 2;

        public static InputFrame From(uint seq, in TankIntent intent)
        {
            Vector2 move = Vector2.ClampMagnitude(intent.Move, 1f);
            float aim = Mathf.Repeat(intent.AimYaw, 360f);
            return new InputFrame
            {
                Seq = seq,
                MoveX = (sbyte)Mathf.RoundToInt(move.x * 127f),
                MoveZ = (sbyte)Mathf.RoundToInt(move.y * 127f),
                Aim = (ushort)Mathf.Clamp(Mathf.RoundToInt(aim / 360f * 65536f), 0, 65535),
                Buttons = (byte)((intent.Fire ? FireBit : 0) | (intent.Dash ? DashBit : 0)),
            };
        }

        public TankIntent ToIntent()
        {
            return new TankIntent
            {
                Move = new Vector2(MoveX / 127f, MoveZ / 127f),
                AimYaw = Aim * (360f / 65536f),
                Fire = (Buttons & FireBit) != 0,
                Dash = (Buttons & DashBit) != 0,
            };
        }
    }

    /// <summary>The authoritative state of one tank at one server tick, as sent to every client.</summary>
    public struct TankSnapshot
    {
        public int Id;
        public uint AckSeq;             // the last input of this tank the server has applied
        public float X, Z, VX, VZ;
        public ushort Yaw, Turret;      // 0..65535 = 0..360 degrees
        public float DashTime, DashCooldown, DashYaw;
        public byte Hp;
        public byte Weapon, Ammo;
        public ushort Cores;                // bit per core the tank has
        public float SpeedTime, DamageTime; // seconds left of the speed and damage items
        public byte Shield;                 // shield points left

        public static ushort PackAngle(float degrees) { return (ushort)Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 360f * 65536f), 0, 65535); }
        public static float UnpackAngle(ushort packed) { return packed * (360f / 65536f); }

        public static TankSnapshot Capture(TankModel t, uint ackSeq)
        {
            return new TankSnapshot
            {
                Id = t.Id, AckSeq = ackSeq, X = t.Body.Position.x, Z = t.Body.Position.y, VX = t.Body.Velocity.x, VZ = t.Body.Velocity.y,
                Yaw = PackAngle(t.Body.Yaw), Turret = PackAngle(t.TurretYaw),
                DashTime = t.Dash.Time, DashCooldown = t.Dash.Cooldown, DashYaw = t.Dash.Yaw,
                Hp = (byte)Mathf.Clamp(t.Health.Current, 0, 255), Weapon = (byte)t.Weapon.WeaponId, Ammo = (byte)Mathf.Clamp(t.Weapon.Ammo, 0, 255),
                Cores = t.Cores, SpeedTime = t.Status.SpeedTime, DamageTime = t.Status.DamageTime, Shield = (byte)Mathf.Clamp(t.Status.Shield, 0, 255),
            };
        }

        /// <summary>Overwrites the parts of a tank the simulation owns (not health or weapon).</summary>
        public void ApplyMotion(TankModel t)
        {
            t.Body.Position = new Vector2(X, Z);
            t.Body.Velocity = new Vector2(VX, VZ);
            t.Body.Yaw = UnpackAngle(Yaw);
            t.TurretYaw = UnpackAngle(Turret);
            t.Dash = new DashState { Time = DashTime, Cooldown = DashCooldown, Yaw = DashYaw };
            t.Status.SpeedTime = SpeedTime;
        }
    }
}
