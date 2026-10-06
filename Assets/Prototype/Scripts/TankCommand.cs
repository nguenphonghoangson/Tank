using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// One frame of intent for a tank. Local input, bots and (later) network input all produce this
    /// struct; TankUnit only ever reads it. Nothing in the simulation knows where it came from.
    /// </summary>
    public struct TankCommand
    {
        /// <summary>Desired hull direction on the ground plane (x = world X, y = world Z), magnitude 0..1.</summary>
        public Vector2 Move;

        /// <summary>World-space point the turret should face. Mobile can derive it from an aim stick.</summary>
        public Vector3 AimPoint;

        public bool Fire;
        public bool Reload;
        public bool Skill;
    }
}
