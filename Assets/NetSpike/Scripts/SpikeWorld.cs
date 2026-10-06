using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>The static arena as seen by the simulation: slide-along-walls resolution and ray tests. Same scene on server and clients.</summary>
    public sealed class SpikeWorld : MonoBehaviour
    {
        static SpikeWorld s_I;
        static readonly Collider[] s_Buf = new Collider[16];
        const int Mask = ~(1 << 2);                 // everything except "Ignore Raycast", where the probe lives
        static readonly Vector3 Half = new Vector3(1.05f, 0.6f, 1.65f);

        BoxCollider m_Probe;

        void Awake()
        {
            s_I = this;
            var go = new GameObject("SimProbe") { layer = 2 };
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, -500f, 0f);
            m_Probe = go.AddComponent<BoxCollider>();
            m_Probe.size = Half * 2f;
            m_Probe.center = new Vector3(0f, 0.6f, 0f);
        }

        /// <summary>Push the tank box out of any wall or cover it overlaps (so it slides instead of sticking).</summary>
        public static void Resolve(ref Vector3 pos, float yaw)
        {
            if (s_I == null) return;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            for (int it = 0; it < 3; it++)
            {
                int n = Physics.OverlapBoxNonAlloc(pos + Vector3.up * 0.6f, Half, s_Buf, rot, Mask, QueryTriggerInteraction.Ignore);
                bool moved = false;
                SpikeMetrics.DbgOverlaps += n;
                for (int i = 0; i < n; i++)
                {
                    Collider c = s_Buf[i];
                    if (SpikeMetrics.DbgNames.Length < 120 && !SpikeMetrics.DbgNames.Contains(c.name)) SpikeMetrics.DbgNames += c.name + ";";
                    if (c == s_I.m_Probe || c.bounds.max.y <= 0.05f) continue;      // skip the probe and the floor
                    if (Physics.ComputePenetration(s_I.m_Probe, pos, rot, c, c.transform.position, c.transform.rotation, out Vector3 dir, out float dist) && dist > 0f)
                    {
                        dir.y = 0f;
                        pos += dir * dist;
                        moved = true;
                        SpikeMetrics.DbgPush++;
                    }
                }
                if (!moved) break;
            }
            pos.y = 0f;
        }

        /// <summary>Distance to the first wall along a horizontal ray, or maxDist.</summary>
        public static float RayDistance(Vector3 origin, Vector3 dir, float maxDist)
        {
            return Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, Mask, QueryTriggerInteraction.Ignore) ? hit.distance : maxDist;
        }
    }
}
