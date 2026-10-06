using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>Everything the movement simulation needs. Server and client run the same Step on the same data.</summary>
    public struct TankState
    {
        public Vector3 pos;
        public float yaw, turretYaw, speed;
        public float fireCd;     // seconds until the next shot is allowed (part of the shared simulation)
        public float dashTime, dashCd, dashYaw;   // dash skill: time left in the dash, cooldown left, heading of the dash
        public float speedTime;                   // speed item: seconds left (the multiplier is a constant)
        public byte weapon, ammo;                 // 0 = cannon (unlimited), otherwise a special weapon with ammo left
    }

    /// <summary>One tick of player intent, quantised so both sides apply identical numbers.</summary>
    public struct InputCmd
    {
        public uint seq;          // increases by one per client tick
        public sbyte moveX, moveZ; // -127..127
        public ushort aim;        // turret yaw, 0..65535 = 0..360 degrees
        public byte buttons;      // bit 0 = fire, bit 1 = dash
        public uint viewTick;     // server tick the client was displaying remote tanks at (for lag compensation)
    }

    /// <summary>Authoritative state of one tank, sent by the server every tick.</summary>
    public struct Snap
    {
        public uint serverTick;
        public uint ackSeq;       // last input the server applied for this tank
        public float x, z, speed, fireCd, dashTime, dashCd, dashYaw, speedTime;
        public ushort yaw, turret;
        public byte hp, shield, weapon, ammo;
        public byte flags;        // bit 0 = dead, bit 1 = damage buff active
        public byte epoch;        // changes when the tank respawns: the client resets its prediction
    }
}
