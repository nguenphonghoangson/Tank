using UnityEngine;

namespace Tank.Core.Input
{
    /// <summary>The arithmetic of an on-screen stick: where the finger is relative to where it landed, as a direction and a strength from 0 to 1.</summary>
    public static class StickMath
    {
        /// <param name="origin">Where the touch began.</param>
        /// <param name="current">Where the finger is now.</param>
        /// <param name="radius">How far the finger must travel for full strength.</param>
        /// <param name="deadZone">Strength below which the stick reads as centred (0..1).</param>
        public static Vector2 Evaluate(Vector2 origin, Vector2 current, float radius, float deadZone)
        {
            if (radius <= 0f) return Vector2.zero;
            Vector2 d = (current - origin) / radius;
            float m = d.magnitude;
            if (m <= deadZone) return Vector2.zero;
            if (m > 1f) { d /= m; m = 1f; }
            // rescale so the strength rises smoothly from 0 at the dead zone edge, rather than jumping to the dead zone value
            float strength = (m - deadZone) / (1f - deadZone);
            return d.normalized * strength;
        }

        public static float YawDegrees(Vector2 direction) { return Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg; }
    }
}
