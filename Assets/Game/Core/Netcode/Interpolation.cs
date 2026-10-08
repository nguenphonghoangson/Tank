using System.Collections.Generic;
using UnityEngine;

namespace Tank.Core.Netcode
{
    /// <summary>The pose of another player's tank at one moment.</summary>
    public struct RemotePose { public Vector2 Position, Velocity; public float Yaw, Turret; }

    /// <summary>
    /// Smooth motion for tanks this client does not control: keep the last second of snapshots and draw the tank a little in the past,
    /// where there is always a snapshot on each side to blend between.
    /// </summary>
    public sealed class SnapshotInterpolator
    {
        struct Sample { public float Tick; public RemotePose Pose; }
        readonly List<Sample> m_Samples = new List<Sample>(48);

        public int Count => m_Samples.Count;
        public float LatestTick => m_Samples.Count == 0 ? 0f : m_Samples[m_Samples.Count - 1].Tick;

        public void Add(uint tick, in TankSnapshot s)
        {
            if (m_Samples.Count > 0 && tick <= m_Samples[m_Samples.Count - 1].Tick) return;           // late or duplicate datagram
            m_Samples.Add(new Sample
            {
                Tick = tick,
                Pose = new RemotePose { Position = new Vector2(s.X, s.Z), Velocity = new Vector2(s.VX, s.VZ), Yaw = TankSnapshot.UnpackAngle(s.Yaw), Turret = TankSnapshot.UnpackAngle(s.Turret) },
            });
            if (m_Samples.Count > 40) m_Samples.RemoveAt(0);
        }

        public void Clear() { m_Samples.Clear(); }

        public bool TrySample(float tick, out RemotePose pose)
        {
            pose = default;
            if (m_Samples.Count == 0) return false;
            if (tick <= m_Samples[0].Tick) { pose = m_Samples[0].Pose; return true; }
            Sample last = m_Samples[m_Samples.Count - 1];
            if (tick >= last.Tick) { pose = last.Pose; return true; }
            for (int i = m_Samples.Count - 1; i > 0; i--)
            {
                Sample b = m_Samples[i], a = m_Samples[i - 1];
                if (tick < a.Tick) continue;
                float k = (tick - a.Tick) / Mathf.Max(1e-4f, b.Tick - a.Tick);
                // a tank that jumped (respawn) is not blended across the gap
                if ((b.Pose.Position - a.Pose.Position).magnitude > 12f) { pose = k < 0.5f ? a.Pose : b.Pose; return true; }
                pose = new RemotePose
                {
                    Position = Vector2.Lerp(a.Pose.Position, b.Pose.Position, k), Velocity = Vector2.Lerp(a.Pose.Velocity, b.Pose.Velocity, k),
                    Yaw = Mathf.LerpAngle(a.Pose.Yaw, b.Pose.Yaw, k), Turret = Mathf.LerpAngle(a.Pose.Turret, b.Pose.Turret, k),
                };
                return true;
            }
            pose = m_Samples[0].Pose;
            return true;
        }
    }

    /// <summary>Which server tick the screen shows: always a few ticks behind the newest snapshot, easing back toward that target if it drifts.</summary>
    public sealed class RenderTickClock
    {
        public float DelayTicks = 3f;
        public float Tick { get; private set; }
        bool m_Started;

        public void Advance(float latestServerTick)
        {
            float target = latestServerTick - DelayTicks;
            if (!m_Started) { Tick = target; m_Started = true; return; }
            Tick += 1f;                                                   // one client tick is one server tick
            float error = target - Tick;
            if (Mathf.Abs(error) > 4f) Tick = target;                     // far off (a stall): jump
            else Tick += error * 0.1f;                                    // otherwise ease, never visible as a speed change
        }

        public void Reset() { m_Started = false; }
    }
}
