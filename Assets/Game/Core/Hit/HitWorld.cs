using System.Collections.Generic;
using UnityEngine;

namespace Tank.Core.Hit
{
    /// <summary>
    /// The static arena as the simulation sees it: analytic shapes, no Unity colliders and no physics scene. It answers the two
    /// questions gameplay has: "push this circle out of the walls" and "how far does this ray travel before it hits one".
    /// </summary>
    public interface IHitWorld
    {
        /// <summary>Moves the circle out of any shape it overlaps and removes the velocity driving into it, so the tank slides along the wall.</summary>
        void ResolveCircle(ref Vector2 position, ref Vector2 velocity, float radius);
        float Raycast(Vector2 origin, Vector2 direction, float maxDistance);
    }

    /// <summary>One static shape. New shape kinds are new classes; HitWorld does not change (open/closed).</summary>
    public interface IHitShape
    {
        void PushOut(ref Vector2 position, ref Vector2 velocity, float radius);
        float Raycast(Vector2 origin, Vector2 direction, float maxDistance);
    }

    public sealed class HitWorld : IHitWorld
    {
        readonly List<IHitShape> m_Shapes = new List<IHitShape>();
        const int Iterations = 3;     // several shapes can push at once (a corner), a few passes settle it

        public HitWorld() { }
        public HitWorld(IEnumerable<IHitShape> shapes) { m_Shapes.AddRange(shapes); }
        public void Add(IHitShape shape) { m_Shapes.Add(shape); }

        public void ResolveCircle(ref Vector2 position, ref Vector2 velocity, float radius)
        {
            for (int pass = 0; pass < Iterations; pass++)
            {
                Vector2 before = position;
                for (int i = 0; i < m_Shapes.Count; i++) m_Shapes[i].PushOut(ref position, ref velocity, radius);
                if ((position - before).sqrMagnitude < 1e-8f) break;
            }
        }

        public float Raycast(Vector2 origin, Vector2 direction, float maxDistance)
        {
            float best = maxDistance;
            for (int i = 0; i < m_Shapes.Count; i++) best = Mathf.Min(best, m_Shapes[i].Raycast(origin, direction, best));
            return best;
        }
    }

    /// <summary>The playable area: an ellipse centred on the origin that things must stay inside (the arena rim).</summary>
    public sealed class EllipseBoundary : IHitShape
    {
        readonly float m_A, m_B;
        public EllipseBoundary(float semiAxisX, float semiAxisZ) { m_A = semiAxisX; m_B = semiAxisZ; }

        public void PushOut(ref Vector2 p, ref Vector2 v, float radius)
        {
            float a = m_A - radius, b = m_B - radius;
            if (a <= 0f || b <= 0f) return;
            float e = p.x * p.x / (a * a) + p.y * p.y / (b * b);
            if (e <= 1f) return;
            p /= Mathf.Sqrt(e);                                                     // back onto the shrunken ellipse
            var n = new Vector2(p.x / (a * a), p.y / (b * b)).normalized;           // outward normal there
            float into = Vector2.Dot(v, n);
            if (into > 0f) v -= n * into;
        }

        public float Raycast(Vector2 o, Vector2 d, float maxDistance)
        {
            // origin inside: where the ray leaves the ellipse. ((ox + dx t) / a)^2 + ((oz + dz t) / b)^2 = 1
            float qa = d.x * d.x / (m_A * m_A) + d.y * d.y / (m_B * m_B);
            float qb = 2f * (o.x * d.x / (m_A * m_A) + o.y * d.y / (m_B * m_B));
            float qc = o.x * o.x / (m_A * m_A) + o.y * o.y / (m_B * m_B) - 1f;
            if (qc >= 0f) return 0f;                                                // already outside
            if (qa < 1e-9f) return maxDistance;
            float disc = qb * qb - 4f * qa * qc;
            if (disc < 0f) return maxDistance;
            float t = (-qb + Mathf.Sqrt(disc)) / (2f * qa);
            return Mathf.Min(maxDistance, Mathf.Max(0f, t));
        }
    }

    /// <summary>A solid oriented rectangle on the ground plane: cover, rocks, pillars.</summary>
    public sealed class ObbShape : IHitShape
    {
        readonly Vector2 m_Center, m_Half;
        readonly float m_Cos, m_Sin;

        public ObbShape(Vector2 center, Vector2 halfExtents, float yawDegrees)
        {
            m_Center = center; m_Half = halfExtents;
            float r = yawDegrees * Mathf.Deg2Rad;
            m_Cos = Mathf.Cos(r); m_Sin = Mathf.Sin(r);
        }

        // yaw 0 = the box's local z along world +z; local x is world +x rotated by yaw about the vertical axis
        Vector2 ToLocal(Vector2 w) { Vector2 d = w - m_Center; return new Vector2(d.x * m_Cos - d.y * m_Sin, d.x * m_Sin + d.y * m_Cos); }
        Vector2 ToWorldDir(Vector2 l) { return new Vector2(l.x * m_Cos + l.y * m_Sin, -l.x * m_Sin + l.y * m_Cos); }

        public void PushOut(ref Vector2 p, ref Vector2 v, float radius)
        {
            Vector2 l = ToLocal(p);
            Vector2 closest = new Vector2(Mathf.Clamp(l.x, -m_Half.x, m_Half.x), Mathf.Clamp(l.y, -m_Half.y, m_Half.y));
            Vector2 delta = l - closest;
            float dist = delta.magnitude;
            if (dist >= radius) return;

            Vector2 nLocal; float push;
            if (dist > 1e-5f) { nLocal = delta / dist; push = radius - dist; }
            else
            {
                // centre is inside the box: leave through the nearest face
                float px = m_Half.x - Mathf.Abs(l.x), pz = m_Half.y - Mathf.Abs(l.y);
                if (px < pz) { nLocal = new Vector2(l.x >= 0f ? 1f : -1f, 0f); push = px + radius; }
                else { nLocal = new Vector2(0f, l.y >= 0f ? 1f : -1f); push = pz + radius; }
            }
            Vector2 n = ToWorldDir(nLocal);
            p += n * push;
            float into = Vector2.Dot(v, n);
            if (into < 0f) v -= n * into;
        }

        public float Raycast(Vector2 origin, Vector2 dir, float maxDistance)
        {
            Vector2 o = ToLocal(origin);
            Vector2 d = new Vector2(dir.x * m_Cos - dir.y * m_Sin, dir.x * m_Sin + dir.y * m_Cos);
            float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
            for (int axis = 0; axis < 2; axis++)
            {
                float oa = axis == 0 ? o.x : o.y, da = axis == 0 ? d.x : d.y, h = axis == 0 ? m_Half.x : m_Half.y;
                if (Mathf.Abs(da) < 1e-8f) { if (oa < -h || oa > h) return maxDistance; continue; }
                float t1 = (-h - oa) / da, t2 = (h - oa) / da;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1); tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return maxDistance;
            }
            if (tMax < 0f) return maxDistance;                 // box is behind the ray
            if (tMin < 0f) return 0f;                          // origin inside the box: the shot ends at once
            return Mathf.Min(tMin, maxDistance);
        }
    }

    public static class CircleRay
    {
        /// <summary>Distance along a ray to the first point on a circle, or false if it misses within maxDistance.</summary>
        public static bool Hit(Vector2 origin, Vector2 dir, Vector2 center, float radius, float maxDistance, out float distance)
        {
            Vector2 m = origin - center;
            float b = Vector2.Dot(m, dir);
            float c = m.sqrMagnitude - radius * radius;
            distance = 0f;
            if (c > 0f && b > 0f) return false;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float t = Mathf.Max(0f, -b - Mathf.Sqrt(disc));
            if (t > maxDistance) return false;
            distance = t;
            return true;
        }
    }
}
