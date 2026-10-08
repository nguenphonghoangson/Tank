using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>Flat shapes on the ground plane for editor gizmos: hitboxes are invisible in play, so this is how they are seen.</summary>
    public static class GizmoShapes
    {
        const float Lift = 0.06f;       // just above the floor so lines are not buried in it

        public static void Circle(Vector3 center, float radius, Color color, int segments = 40)
        {
            Gizmos.color = color;
            Vector3 prev = center + new Vector3(radius, Lift, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Lift, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        public static void Ellipse(Vector3 center, Vector2 semiAxes, Color color, int segments = 96)
        {
            Gizmos.color = color;
            Vector3 prev = center + new Vector3(semiAxes.x, Lift, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * semiAxes.x, Lift, Mathf.Sin(a) * semiAxes.y);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        /// <summary>A rectangle in the local x/z plane of the given transform.</summary>
        public static void Box(Transform t, Vector3 localCenter, Vector2 halfExtents, Color color)
        {
            Gizmos.color = color;
            Vector3 a = t.TransformPoint(localCenter + new Vector3(-halfExtents.x, 0f, -halfExtents.y));
            Vector3 b = t.TransformPoint(localCenter + new Vector3(-halfExtents.x, 0f, halfExtents.y));
            Vector3 c = t.TransformPoint(localCenter + new Vector3(halfExtents.x, 0f, halfExtents.y));
            Vector3 d = t.TransformPoint(localCenter + new Vector3(halfExtents.x, 0f, -halfExtents.y));
            a.y = b.y = c.y = d.y = t.position.y + Lift;
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
        }
    }
}
