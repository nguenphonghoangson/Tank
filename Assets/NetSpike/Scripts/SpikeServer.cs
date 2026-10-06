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

        public struct PendingHit { public SpikeTank target, shooter; public uint applyTick; public uint fireSeq; public float travel; public int dmg; public bool confirm; public Vector3 impact; public float splashR; public int splashDmg; }

        readonly List<SpikeTank> m_Tanks = new List<SpikeTank>();
        public readonly List<PendingHit> Pending = new List<PendingHit>();
        float m_Acc;

        [Header("Items")]
        public GameObject pickupPrefab, matchPrefab;
        SpikeMatch m_Match;
        public Vector3[] pickupPoints = new Vector3[0];
        public float pickupRespawnScale = 1f;
        readonly List<SpikePickup> m_Pickups = new List<SpikePickup>();
        bool m_PickupsSpawned;
        System.Random m_Rng;

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

        void SpawnPickupsOnce()
        {
            if (m_PickupsSpawned) return;
            m_PickupsSpawned = true;
            m_Rng = new System.Random(12345);
            if (matchPrefab != null) { var mg = Instantiate(matchPrefab); NetworkServer.Spawn(mg); m_Match = mg.GetComponent<SpikeMatch>(); }
            if (pickupPrefab == null) return;
            foreach (Vector3 pos in pickupPoints)
            {
                GameObject go = Instantiate(pickupPrefab, pos, Quaternion.identity);
                NetworkServer.Spawn(go);
                SpikePickup p = go.GetComponent<SpikePickup>();
                p.ServerRoll(m_Rng, true);
                m_Pickups.Add(p);
            }
        }

        void StepTick()
        {
            Tick++;
            SpikeMetrics.ServerTicks++;
            SpawnPickupsOnce();
            foreach (SpikeTank t in m_Tanks) t.ServerStep(Tick);
            foreach (SpikePickup p in m_Pickups)
            {
                p.ServerTick(Time.time, m_Rng);
                if (!p.Available) continue;
                foreach (SpikeTank t in m_Tanks)
                {
                    if (t.ServerDead) continue;
                    Vector3 d = t.ServerState.pos - p.transform.position; d.y = 0f;
                    if (d.sqrMagnitude < SpikePickup.Radius * SpikePickup.Radius && t.ServerApplyPickup(p.CurrentKind)) { SpikeMetrics.PickupsTaken[(int)p.CurrentKind]++; p.ServerTake(Time.time, m_Rng, pickupRespawnScale); break; }
                }
            }
            foreach (SpikeTank t in m_Tanks) t.ServerResolveFire(Tick);
            if (m_Match != null) m_Match.ServerTick(SpikeSim.Dt);

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingHit h = Pending[i];
                if (Tick < h.applyTick) continue;
                Pending.RemoveAt(i);
                if (h.target != null && !h.target.ServerDead) h.target.ServerDamage(h.shooter, h.dmg, h.fireSeq, h.travel, h.confirm);
                if (h.splashR > 0f)
                    foreach (SpikeTank o in m_Tanks)
                    {
                        if (o == h.shooter || o == h.target || o.ServerDead) continue;
                        Vector3 d = o.ServerState.pos - h.impact; d.y = 0f;
                        if (d.magnitude <= h.splashR) o.ServerDamage(h.shooter, h.splashDmg, h.fireSeq, h.travel, false);
                    }
            }
            foreach (SpikeTank t in m_Tanks) t.ServerSendSnapshot(Tick);
        }
    }
}
