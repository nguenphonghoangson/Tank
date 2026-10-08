using Tank.Core.Combat;
using UnityEngine;

namespace Tank.Core.Netcode
{
    /// <summary>
    /// Client-side prediction of the local tank. Every tick the client applies its own input at once and remembers it; when the server's
    /// snapshot arrives, the remembered state at that input is compared with the server's, and a mismatch is fixed by taking the server's
    /// state and replaying every input the server has not seen yet.
    /// </summary>
    public sealed class PredictionBuffer
    {
        const int Capacity = 256;

        struct Entry { public uint Seq; public TankIntent Intent; public Vector2 Position, Velocity; public float Yaw, TurretYaw; public DashState Dash; public bool Valid; }

        readonly Entry[] m_Ring = new Entry[Capacity];
        readonly TankSimulation m_Simulation;
        uint m_LatestSeq;

        public float PositionTolerance = 0.08f;
        public int Corrections { get; private set; }
        public int Reconciliations { get; private set; }

        public PredictionBuffer(TankSimulation simulation) { m_Simulation = simulation; }
        public uint LatestSeq => m_LatestSeq;

        public void Reset() { for (int i = 0; i < Capacity; i++) m_Ring[i].Valid = false; m_LatestSeq = 0; }

        /// <summary>Applies the frame to the tank right now and remembers the outcome.</summary>
        public void Predict(TankModel tank, InputFrame frame, float dt)
        {
            tank.Intent = frame.ToIntent();
            m_Simulation.Step(tank, dt);
            m_Ring[frame.Seq % Capacity] = Capture(frame.Seq, tank);
            m_LatestSeq = frame.Seq;
        }

        /// <returns>true when the prediction was wrong and the tank was corrected.</returns>
        public bool Reconcile(TankModel tank, in TankSnapshot server, float dt)
        {
            if (server.AckSeq == 0 || m_LatestSeq == 0) return false;
            Reconciliations++;
            Entry at = m_Ring[server.AckSeq % Capacity];
            if (!at.Valid || at.Seq != server.AckSeq)
            {
                // the server is behind what we still remember (or far ahead): trust it outright
                server.ApplyMotion(tank);
                return true;
            }

            bool wrong = (at.Position - new Vector2(server.X, server.Z)).magnitude > PositionTolerance
                         || Mathf.Abs(Mathf.DeltaAngle(at.Yaw, TankSnapshot.UnpackAngle(server.Yaw))) > 2f
                         || Mathf.Abs(at.Dash.Cooldown - server.DashCooldown) > 0.2f;
            if (!wrong) return false;

            Corrections++;
            server.ApplyMotion(tank);
            for (uint seq = server.AckSeq + 1; seq <= m_LatestSeq; seq++)
            {
                Entry e = m_Ring[seq % Capacity];
                if (!e.Valid || e.Seq != seq) continue;
                tank.Intent = e.Intent;
                m_Simulation.Step(tank, dt);
                m_Ring[seq % Capacity] = Capture(seq, tank);
            }
            return true;
        }

        static Entry Capture(uint seq, TankModel t)
        {
            return new Entry { Seq = seq, Intent = t.Intent, Position = t.Body.Position, Velocity = t.Body.Velocity, Yaw = t.Body.Yaw, TurretYaw = t.TurretYaw, Dash = t.Dash, Valid = true };
        }
    }
}
