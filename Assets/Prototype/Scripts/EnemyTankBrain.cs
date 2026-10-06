using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// Deliberately dumb enemy: circles the player at a fixed radius, aims with a little lead and fires
    /// when roughly aligned. It writes the same TankCommand as the player input. No pathfinding, no randomness.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class EnemyTankBrain : MonoBehaviour
    {
        public TankUnit self;
        public TankUnit target;
        public float orbitRadius = 11f;
        public float moveScale = 0.6f;
        public float engageRange = 22f;
        public float aimToleranceDeg = 7f;
        public float arenaHalfSize = 26f;

        static readonly RaycastHit[] s_Probe = new RaycastHit[6];
        float m_AvoidUntil;
        float m_AvoidSign = 1f;

        void Update()
        {
            if (self.IsDead || target.IsDead)
            {
                self.Command = default;
                return;
            }

            Vector3 me = self.transform.position;
            Vector3 tp = target.transform.position;
            Vector3 rel = me - tp;
            rel.y = 0f;
            float dist = rel.magnitude;

            // circle the player; reverse direction every 4 s so the strafing is readable but not a pattern to memorise
            float side = (((int)(Time.time / 4f)) & 1) == 0 ? 1f : -1f;
            Vector3 radial = dist > 0.1f ? rel / dist : Vector3.forward;
            Vector3 tangent = new Vector3(radial.z, 0f, -radial.x) * side;
            Vector3 goal = tp + radial * orbitRadius + tangent * 6f;
            goal.x = Mathf.Clamp(goal.x, -arenaHalfSize, arenaHalfSize);
            goal.z = Mathf.Clamp(goal.z, -arenaHalfSize, arenaHalfSize);
            Vector3 to = goal - me;
            to.y = 0f;

            float lead = Mathf.Clamp(dist / Mathf.Max(1f, self.projectileSpeed), 0f, 1f) * 0.6f;
            Vector3 aimPoint = tp + target.Body.linearVelocity * lead;

            Vector3 aimDir = aimPoint - self.turretPivot.position;
            aimDir.y = 0f;
            float err = Vector3.Angle(self.turretPivot.forward, aimDir);

            Vector3 dir = to.sqrMagnitude > 1f ? to.normalized : Vector3.zero;
            if (dir != Vector3.zero && Blocked(me, dir))
            {
                // whisker steering: slide around obstacles; keep the chosen side for a moment so it does not jitter
                if (Time.time >= m_AvoidUntil)
                {
                    Vector3 left = Quaternion.Euler(0f, -70f, 0f) * dir;
                    Vector3 right = Quaternion.Euler(0f, 70f, 0f) * dir;
                    m_AvoidSign = !Blocked(me, left) ? -1f : (!Blocked(me, right) ? 1f : m_AvoidSign);
                    m_AvoidUntil = Time.time + 0.7f;
                }
                dir = Quaternion.Euler(0f, 70f * m_AvoidSign, 0f) * dir;
            }

            TankCommand cmd = self.Command;
            cmd.Move = new Vector2(dir.x, dir.z) * moveScale;
            cmd.AimPoint = aimPoint;
            cmd.Fire = dist < engageRange && err < aimToleranceDeg;
            self.Command = cmd;
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
