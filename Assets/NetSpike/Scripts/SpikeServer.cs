using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>Server tick loop: step every tank, resolve shots with lag compensation, apply delayed hits, send snapshots. Runs only while the server is active.</summary>
    public sealed class SpikeServer : MonoBehaviour
    {
        public static SpikeServer I { get; private set; }
        public uint Tick { get; private set; }

        public struct PendingHit { public SpikeTank target, shooter; public uint applyTick; public uint fireSeq; public float travel; }

        readonly List<SpikeTank> m_Tanks = new List<SpikeTank>();
        public readonly List<PendingHit> Pending = new List<PendingHit>();
        float m_Acc;

        public Vector3[] spawnPoints =
        {
            new Vector3(-22f, 0f, -22f), new Vector3(22f, 0f, 22f), new Vector3(22f, 0f, -22f), new Vector3(-22f, 0f, 22f),
        };

        void Awake() { I = this; }

        public void Register(SpikeTank t) { if (!m_Tanks.Contains(t)) m_Tanks.Add(t); }
        public void Unregister(SpikeTank t) { m_Tanks.Remove(t); }
        public IReadOnlyList<SpikeTank> Tanks => m_Tanks;

        public Vector3 PickSpawn(SpikeTank forTank)
        {
            Vector3 best = spawnPoints[0];
            float bestD = -1f;
            foreach (Vector3 p in spawnPoints)
            {
                float nearest = float.MaxValue;
                foreach (SpikeTank o in m_Tanks) if (o != forTank && !o.ServerDead) nearest = Mathf.Min(nearest, Vector3.Distance(p, o.ServerState.pos));
                if (nearest > bestD) { bestD = nearest; best = p; }
            }
            return best;
        }

        void Update()
        {
            if (!NetworkServer.active) return;
            m_Acc += Time.unscaledDeltaTime;
            int guard = 0;
            while (m_Acc >= SpikeSim.Dt && guard++ < 5)
            {
                m_Acc -= SpikeSim.Dt;
                StepTick();
            }
        }

        void StepTick()
        {
            Tick++;
            SpikeMetrics.ServerTicks++;
            foreach (SpikeTank t in m_Tanks) t.ServerStep(Tick);
            foreach (SpikeTank t in m_Tanks) t.ServerResolveFire(Tick);

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingHit h = Pending[i];
                if (Tick < h.applyTick) continue;
                Pending.RemoveAt(i);
                if (h.target != null && !h.target.ServerDead) h.target.ServerDamage(h.shooter, SpikeSim.Damage, h.fireSeq, h.travel);
            }
            foreach (SpikeTank t in m_Tanks) t.ServerSendSnapshot(Tick);
        }
    }
}
