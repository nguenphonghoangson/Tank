using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Cores;
using Tank.Core.Common;
using Tank.Core.Events;
using Tank.Core.Hit;
using Tank.Core.Input;
using Tank.Core.Items;
using Tank.Core.Movement;
using Tank.Core.Netcode;
using Tank.Gameplay;
using Tank.Gameplay.Flow;
using UnityEngine;

namespace Tank.Net
{
    /// <summary>
    /// A client's fixed step. It does not run the rules, the server does. It (1) predicts the local tank from the player's own input and
    /// sends that input up, (2) corrects the prediction when the server disagrees, (3) draws everyone else a few ticks in the past, and
    /// (4) keeps shots in the air moving. Nothing here can hurt a tank; damage only ever comes from the server.
    /// </summary>
    public sealed class ClientSimulationDriver : ISimulationDriver
    {
        readonly SimulationClock m_Clock;
        readonly TankRegistry m_Registry;
        readonly ClientRoster m_Roster;
        readonly ProjectileTracker m_Tracker;
        readonly LocalPlayer m_Local;
        readonly IHitWorld m_World;
        readonly IRandom m_Random;
        readonly ICoreCatalog m_Cores;
        readonly IItemQuery m_Items;
        readonly Tank.Gameplay.Input.PlayerCommandSource m_Player;
        readonly PredictionBuffer m_Prediction;
        readonly CommandCollector m_Collector = new CommandCollector();
        readonly Dictionary<int, SnapshotInterpolator> m_Remote = new Dictionary<int, SnapshotInterpolator>();
        readonly RenderTickClock m_RenderClock = new RenderTickClock();
        readonly InputFrame[] m_Recent = new InputFrame[3];
        readonly InputFrame[] m_Send = new InputFrame[3];
        ICommandSource m_Source;
        TankSnapshot m_LocalSnapshot;
        bool m_HasLocalSnapshot;
        uint m_LatestTick, m_Seq;

        public int SnapshotsIn { get; private set; }
        public int Corrections => m_Prediction.Corrections;
        public int Reconciliations => m_Prediction.Reconciliations;

        public ClientSimulationDriver(SimulationClock clock, TankRegistry registry, ClientRoster roster, ProjectileTracker tracker, LocalPlayer local,
            IMovementModel movement, IHitWorld world, MovementSettings moveSettings, TankSettings tankSettings, IRandom random, ICoreCatalog cores, IItemQuery items, Tank.Gameplay.Input.PlayerCommandSource player)
        {
            m_Player = player; m_Cores = cores; m_Items = items;
            m_Clock = clock; m_Registry = registry; m_Roster = roster; m_Tracker = tracker; m_Local = local; m_World = world; m_Random = random;
            // predicting must not announce anything: a replay runs the same dash twice, and the server's own events already tell everyone
            m_Prediction = new PredictionBuffer(new TankSimulation(movement, world, moveSettings, tankSettings, new NullEventBus()));
        }

        // ------------------------------------------------------------------ what arrives

        public void OnSnapshot(uint tick, TankSnapshot[] snapshots)
        {
            if (tick <= m_LatestTick) return;                                        // an older datagram arriving late
            m_LatestTick = tick; SnapshotsIn++;
            for (int i = 0; i < snapshots.Length; i++)
            {
                TankSnapshot s = snapshots[i];
                TankModel tank = m_Roster.Resolve(s, true);
                if (tank == null) continue;
                if (tank.Cores != s.Cores) tank.SetCores(s.Cores, m_Cores);          // cores change what a tank can do, so the prediction needs them too
                tank.Health.Set(s.Hp);
                tank.Status.DamageTime = s.DamageTime; tank.Status.Shield = s.Shield;
                if (s.Id != m_Local.TankId || Mathf.Abs(tank.Status.SpeedTime - s.SpeedTime) > 0.3f) tank.Status.SpeedTime = s.SpeedTime;
                tank.Weapon.WeaponId = s.Weapon; tank.Weapon.Ammo = s.Ammo;
                if (s.Id == m_Local.TankId) { m_LocalSnapshot = s; m_HasLocalSnapshot = true; continue; }
                if (!m_Remote.TryGetValue(s.Id, out SnapshotInterpolator interp)) m_Remote[s.Id] = interp = new SnapshotInterpolator();
                interp.Add(tick, s);
            }
        }

        public void OnRespawned(int id, Vector2 position, float yaw)
        {
            TankModel t = m_Registry.Find(id);
            if (t == null) return;
            t.Body = new KinematicState { Position = position, Yaw = yaw };
            t.TurretYaw = yaw; t.Dash = default; t.Weapon.Reset();
            if (id == m_Local.TankId) { m_Prediction.Reset(); m_HasLocalSnapshot = false; }
            else if (m_Remote.TryGetValue(id, out SnapshotInterpolator interp)) interp.Clear();          // do not slide in from where it died
        }

        // ------------------------------------------------------------------ each tick

        public void Tick(float dt)
        {
            m_Clock.Tick(dt);
            TankModel me = m_Registry.Find(m_Local.TankId);
            NetTank net = NetTank.Local;
            if (me != null && net != null) StepLocal(me, net, dt);

            if (m_LatestTick > 0)
            {
                m_RenderClock.Advance(m_LatestTick);
                foreach (KeyValuePair<int, SnapshotInterpolator> kv in m_Remote)
                {
                    TankModel t = m_Registry.Find(kv.Key);
                    if (t == null || !kv.Value.TrySample(m_RenderClock.Tick, out RemotePose pose)) continue;
                    t.Body.Position = pose.Position; t.Body.Velocity = pose.Velocity; t.Body.Yaw = pose.Yaw; t.TurretYaw = pose.Turret;
                }
            }
            m_Tracker.Tick(dt);
        }

        void StepLocal(TankModel me, NetTank net, float dt)
        {
            if (m_HasLocalSnapshot) { m_Prediction.Reconcile(me, m_LocalSnapshot, dt); m_HasLocalSnapshot = false; }

            m_Source ??= LocalInputSource.Create(m_Registry, m_World, m_Clock, m_Random, m_Items, m_Player);
            TankIntent intent = m_Collector.Collect(m_Source, me, me.Intent);
            InputFrame frame = InputFrame.From(++m_Seq, intent);
            m_Prediction.Predict(me, frame, dt);

            // the last three frames travel together, so one lost datagram costs nothing
            m_Recent[0] = m_Recent[1]; m_Recent[1] = m_Recent[2]; m_Recent[2] = frame;
            for (int i = 0; i < 3; i++) m_Send[i] = m_Recent[i].Seq == 0 ? frame : m_Recent[i];
            net.SendInput(m_Send);
        }

        public string StatsLine()
        {
            return "snapshots " + SnapshotsIn + " reconciled " + Reconciliations + " corrected " + Corrections + " remote " + m_Remote.Count;
        }
    }
}
