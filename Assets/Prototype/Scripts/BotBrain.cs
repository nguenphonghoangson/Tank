using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// Simulated player. Deliberately simple and deterministic: choose an objective (a point that is not securely ours,
    /// spreading out from teammates), fight any enemy it can see nearby, repair when hurt, and write a TankCommand.
    /// No pathfinding: it steers around obstacles with whiskers.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class BotBrain : MonoBehaviour
    {
        public TankUnit self;
        public MatchManager match;
        public int index;

        [Header("Behaviour")]
        public float sightRange = 30f;
        public float engageRange = 20f;
        public float preferredDistance = 10f;
        public float aimToleranceDeg = 7f;
        public float minFireGap = 0.6f;
        public float moveScale = 0.85f;
        public float thinkInterval = 0.3f;

        public ControlPoint CurrentPoint => m_Point;

        static readonly RaycastHit[] s_Probe = new RaycastHit[8];

        TankUnit m_Target;
        ControlPoint m_Point;
        Pickup m_Pickup;
        float m_NextThink, m_NextFire, m_AvoidUntil, m_AvoidSign = 1f;

        void Start() { self.Fired += OnFired; }
        void OnDestroy() { if (self != null) self.Fired -= OnFired; }
        void OnFired(TankUnit u) { m_NextFire = Time.time + minFireGap + (index * 37 % 10) * 0.02f; }

        void Update()
        {
            if (self.IsDead || match.State != MatchManager.MatchState.Playing) { self.Command = default; return; }
            if (Time.time >= m_NextThink)
            {
                Think();
                m_NextThink = Time.time + thinkInterval + 0.06f * (index % 4);
            }
            Act();
        }

        // ------------------------------------------------------------------ decisions (a few times per second)

        void Think()
        {
            Vector3 me = self.transform.position;

            // nearest visible enemy
            m_Target = null;
            float best = sightRange;
            foreach (TankUnit t in match.Tanks)
            {
                if (t == self || t.IsDead || t.team == self.team) continue;
                float d = Flat(t.transform.position - me).magnitude;
                if (d < best && CanSee(t)) { best = d; m_Target = t; }
            }

            // repair when hurt; otherwise grab a useful item that is close (weapons and buffs), unless already fighting
            m_Pickup = null;
            bool hurt = self.Hp < self.maxHp * 0.45f;
            float pd = hurt ? 40f : 24f;
            foreach (Pickup p in match.Layout.pickups)
            {
                if (!p.Available) continue;
                if (hurt ? p.kind != PickupKind.Repair : (p.kind == PickupKind.Repair || m_Target != null)) continue;
                if (p.kind == PickupKind.Weapon && self.HasSpecialWeapon) continue;
                float d = Flat(p.transform.position - me).magnitude;
                if (d < pd) { pd = d; m_Pickup = p; }
            }

            // objective: prefer points that are not securely ours, near, and not already taken by a teammate
            ControlPoint[] points = match.Layout.controlPoints;
            float bestScore = float.MinValue;
            ControlPoint choice = m_Point;
            for (int i = 0; i < points.Length; i++)
            {
                ControlPoint p = points[i];
                float s = 0f;
                if (p.Owner != self.team) s += 2f;
                else
                {
                    // defend what we own: weak or contested points first, and points with an enemy nearby
                    s += (1f - p.Model.OwnerHold) * 2.5f + (p.Contested ? 1.2f : 0f);
                    foreach (TankUnit e in match.Tanks)
                        if (!e.IsDead && e.team != self.team && p.Contains(e.transform.position, 14f)) { s += 1.2f; break; }
                }
                s -= Flat(p.transform.position - me).magnitude * 0.04f;
                if (index % points.Length == i) s += 0.5f;
                if (p == m_Point) s += 0.6f;
                foreach (BotBrain other in match.Bots)
                    if (other != null && other != this && other.self.team == self.team && other.CurrentPoint == p) s -= 0.5f;
                if (s > bestScore) { bestScore = s; choice = p; }
            }
            m_Point = choice;
        }

        // ------------------------------------------------------------------ per-frame behaviour

        void Act()
        {
            Vector3 me = self.transform.position;
            TankCommand cmd = self.Command;
            Vector3 goal = me;
            bool holding = false;

            if (m_Pickup != null && m_Pickup.Available) goal = m_Pickup.transform.position;
            else if (m_Point != null)
            {
                goal = m_Point.transform.position;
                holding = Flat(goal - me).magnitude < m_Point.radius * 0.5f;
            }

            Vector3 aimPoint = me + self.transform.forward * 10f;
            cmd.Fire = false;
            if (m_Target != null && !m_Target.IsDead)
            {
                Vector3 tp = m_Target.transform.position;
                Vector3 rel = Flat(me - tp);
                float dist = rel.magnitude;
                float lead = Mathf.Clamp(dist / Mathf.Max(1f, self.ProjectileSpeed), 0f, 1f) * 0.7f;
                aimPoint = tp + m_Target.Body.linearVelocity * lead + Vector3.up * 0.8f;

                if (dist < engageRange)
                {
                    // fight: strafe around the target and keep a comfortable distance, unless we are standing on our point
                    Vector3 radial = dist > 0.1f ? rel / dist : Vector3.forward;
                    float side = (((int)(Time.time / 2.5f + index)) & 1) == 0 ? 1f : -1f;
                    Vector3 tangent = new Vector3(radial.z, 0f, -radial.x) * side;
                    float push = dist < preferredDistance ? 1f : (dist > preferredDistance + 4f ? -1f : 0f);
                    Vector3 want = tangent + radial * push * 0.8f;
                    goal = holding ? me + tangent * 3f : me + want.normalized * 6f;
                }

                Vector3 aimDir = Flat(aimPoint - self.turretPivot.position);
                float err = Vector3.Angle(self.turretPivot.forward, aimDir);
                cmd.Fire = dist < self.weapon.range * 0.8f && err < aimToleranceDeg && Time.time >= m_NextFire;
            }

            Vector3 to = Flat(goal - me);
            Vector3 dir = to.sqrMagnitude > 2.25f ? to.normalized : Vector3.zero;
            if (dir != Vector3.zero && Blocked(me, dir))
            {
                // whisker steering; keep the chosen side for a moment so it does not jitter
                if (Time.time >= m_AvoidUntil)
                {
                    Vector3 left = Quaternion.Euler(0f, -70f, 0f) * dir;
                    Vector3 right = Quaternion.Euler(0f, 70f, 0f) * dir;
                    m_AvoidSign = !Blocked(me, left) ? -1f : (!Blocked(me, right) ? 1f : m_AvoidSign);
                    m_AvoidUntil = Time.time + 0.7f;
                }
                dir = Quaternion.Euler(0f, 70f * m_AvoidSign, 0f) * dir;
            }

            cmd.Move = new Vector2(dir.x, dir.z) * moveScale;
            if (m_Target == null && dir != Vector3.zero) aimPoint = me + dir * 10f;
            cmd.AimPoint = aimPoint;
            cmd.Reload = self.Ammo < self.MagazineSize * 0.3f && m_Target == null;
            self.Command = cmd;
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        bool CanSee(TankUnit t)
        {
            Vector3 from = self.transform.position + Vector3.up * 1.2f;
            Vector3 to = t.transform.position + Vector3.up * 1.0f;
            Vector3 d = to - from;
            float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, s_Probe, len, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            TankUnit first = null;
            for (int i = 0; i < n; i++)
            {
                TankUnit h = s_Probe[i].collider.GetComponentInParent<TankUnit>();
                if (h == self) continue;
                if (s_Probe[i].distance < nearest) { nearest = s_Probe[i].distance; first = h; }
            }
            return nearest == float.MaxValue || first == t;
        }

        bool Blocked(Vector3 from, Vector3 dir)
        {
            // start beyond our own hull, above the floor; only static scenery counts (tanks are not obstacles here)
            int n = Physics.SphereCastNonAlloc(from + dir * 2.0f + Vector3.up * 1.2f, 0.7f, dir, s_Probe, 4f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (s_Probe[i].collider.GetComponentInParent<TankUnit>() == null) return true;
            return false;
        }
    }
}
