using UnityEngine;

namespace Tank.Core.Movement
{
    /// <summary>Where a tank is and how it moves. Plain data, so the same step can run on a server, a predicting client and in tests.</summary>
    public struct KinematicState
    {
        public Vector2 Position;     // x, z on the ground plane
        public Vector2 Velocity;
        public float Yaw;            // degrees, 0 = +z
        public float Speed => Velocity.magnitude;
    }

    public readonly struct MovementSettings
    {
        public readonly float MaxSpeed, Acceleration, Braking, RotationSpeed, Radius;
        public MovementSettings(float maxSpeed, float acceleration, float braking, float rotationSpeed, float radius)
        {
            MaxSpeed = maxSpeed; Acceleration = acceleration; Braking = braking; RotationSpeed = rotationSpeed; Radius = radius;
        }
    }

    /// <summary>Strategy: how input turns into velocity and heading. Collision is somebody else's job (IHitWorld).</summary>
    public interface IMovementModel
    {
        /// <param name="move">Stick or key direction, magnitude 0..1.</param>
        /// <param name="speedMultiplier">Buffs and cores scale the top speed only.</param>
        void Step(ref KinematicState state, Vector2 move, float speedMultiplier, in MovementSettings settings, float dt);
    }

    /// <summary>
    /// Direct-drive movement in the style of a networked character controller: input accelerates the velocity vector up to a top speed,
    /// releasing it brakes by a fraction per second, and the hull turns toward the travel direction at a fixed rate.
    /// </summary>
    public sealed class DirectDriveMovement : IMovementModel
    {
        public void Step(ref KinematicState state, Vector2 move, float speedMultiplier, in MovementSettings settings, float dt)
        {
            if (move.sqrMagnitude > 1f) move.Normalize();
            float top = settings.MaxSpeed * speedMultiplier;

            if (move.sqrMagnitude > 0.0025f)
            {
                Vector2 v = state.Velocity + move * (settings.Acceleration * dt);
                float cap = top * move.magnitude;
                float speed = v.magnitude;
                if (speed > cap) v = v / speed * Mathf.Max(cap, speed - settings.Acceleration * dt);    // over the cap (buff ended): ease down, do not snap
                state.Velocity = v;
                float target = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg;
                state.Yaw = Mathf.LerpAngle(state.Yaw, target, Mathf.Clamp01(settings.RotationSpeed * dt));
            }
            else
            {
                state.Velocity = Vector2.Lerp(state.Velocity, Vector2.zero, Mathf.Clamp01(settings.Braking * dt));
            }
            state.Position += state.Velocity * dt;
        }
    }
}
