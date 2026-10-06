using System.Collections.Generic;
using Mirror;
using TankGame.Prototype;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>State of one flag as sent to clients (hold and progress quantised to 0..255).</summary>
    public struct FlagSync { public sbyte owner, capturer; public byte hold, progress; public bool contested; }

    /// <summary>
    /// The objective layer: flags captured by standing in a zone (the prototype's CapturePointModel, run on the server), score from
    /// captures, kills and flag income, and a match clock. Two teams, a player's team is its netId parity.
    /// </summary>
    public sealed class SpikeMatch : NetworkBehaviour
    {
        public static SpikeMatch I { get; private set; }
        public const float MatchSeconds = 300f, CaptureSeconds = 10f, DecaySeconds = 45f, IncomePerSecond = 3f, RestartAfter = 10f;
        public const int CapturePoints = 100, KillPoints = 50;
        public static readonly Color[] TeamColors = { new Color(0.3f, 0.7f, 1f), new Color(1f, 0.4f, 0.35f) };
        public static readonly string[] TeamNames = { "BLUE", "RED" };
        public static int TeamOf(uint netId) { return (int)(netId % 2u); }

        public readonly SyncList<FlagSync> flags = new SyncList<FlagSync>();
        [SyncVar] public int score0, score1;
        [SyncVar] public int timeLeft = (int)MatchSeconds;
        [SyncVar] public int winner = -2;                  // -2 running, -1 draw, otherwise the winning team

        public ControlPoint[] Points { get; private set; } = new ControlPoint[0];
        float m_TimeLeft = MatchSeconds, m_EndTimer;
        readonly float[] m_Income = new float[2];
        readonly int[] m_Counts = new int[2];

        static ControlPoint[] FindPoints()
        {
            var pts = Object.FindObjectsByType<ControlPoint>(FindObjectsSortMode.None);
            System.Array.Sort(pts, (a, b) => string.CompareOrdinal(a.label, b.label));
            return pts;
        }

        public override void OnStartServer()
        {
            I = this;
            Points = FindPoints();
            foreach (ControlPoint p in Points) { p.Model.CaptureSeconds = CaptureSeconds; p.Model.DecaySeconds = DecaySeconds; p.Model.Reset(); flags.Add(new FlagSync { owner = -1, capturer = -1 }); }
        }

        public override void OnStartClient()
        {
            I = this;
            Points = FindPoints();
            if (!Application.isBatchMode) foreach (ControlPoint p in Points) p.Init(TeamColors, CaptureSeconds, DecaySeconds);
        }

        public override void OnStopServer() { if (I == this) I = null; }
        public override void OnStopClient() { if (I == this) I = null; }

        // ---- server
        public void ServerAdd(int team, int points)
        {
            if (winner != -2 || team < 0 || team > 1) return;
            if (team == 0) score0 += points; else score1 += points;
        }

        public void ServerKill(SpikeTank shooter, SpikeTank victim)
        {
            int a = TeamOf(shooter.netId), b = TeamOf(victim.netId);
            if (a != b) ServerAdd(a, KillPoints);
        }

        public void ServerTick(float dt)
        {
            if (winner != -2)
            {
                m_EndTimer -= dt;
                if (m_EndTimer <= 0f) ServerRestart();
                return;
            }
            m_TimeLeft -= dt;
            timeLeft = Mathf.Max(0, Mathf.CeilToInt(m_TimeLeft));
            for (int i = 0; i < Points.Length; i++)
            {
                ControlPoint p = Points[i];
                m_Counts[0] = m_Counts[1] = 0;
                foreach (SpikeTank t in SpikeServer.I.Tanks) if (!t.ServerDead && p.Contains(t.ServerState.pos)) m_Counts[TeamOf(t.netId)]++;
                CapturePointModel.Change ch = p.Model.Tick(dt, m_Counts, out int team);
                if (ch == CapturePointModel.Change.Captured) { ServerAdd(team, CapturePoints); SpikeMetrics.FlagsCaptured++; }
                if (p.Model.Owner >= 0) m_Income[p.Model.Owner] += IncomePerSecond * p.weight * dt;

                CapturePointModel m = p.Model;
                var f = new FlagSync { owner = (sbyte)m.Owner, capturer = (sbyte)m.Capturer, hold = (byte)Mathf.RoundToInt(Mathf.Clamp01(m.OwnerHold) * 255f), progress = (byte)Mathf.RoundToInt(Mathf.Clamp01(m.CapProgress) * 255f), contested = m.Contested };
                FlagSync old = flags[i];
                if (old.owner != f.owner || old.capturer != f.capturer || old.contested != f.contested || Mathf.Abs(old.hold - f.hold) >= 3 || Mathf.Abs(old.progress - f.progress) >= 3) flags[i] = f;
            }
            for (int t = 0; t < 2; t++) { int whole = (int)m_Income[t]; if (whole > 0) { m_Income[t] -= whole; ServerAdd(t, whole); } }
            if (m_TimeLeft <= 0f)
            {
                winner = score0 > score1 ? 0 : (score1 > score0 ? 1 : -1);
                m_EndTimer = RestartAfter;
            }
        }

        void ServerRestart()
        {
            score0 = score1 = 0; m_Income[0] = m_Income[1] = 0f;
            m_TimeLeft = MatchSeconds; timeLeft = (int)MatchSeconds; winner = -2;
            foreach (ControlPoint p in Points) p.Model.Reset();
        }

        // ---- client: copy the synced state into the prototype's ControlPoint so its own visuals draw it
        void Update()
        {
            if (!isClient || flags.Count != Points.Length) return;
            for (int i = 0; i < Points.Length; i++)
            {
                FlagSync f = flags[i]; CapturePointModel m = Points[i].Model;
                m.Owner = f.owner; m.Capturer = f.capturer; m.OwnerHold = f.hold / 255f; m.CapProgress = f.progress / 255f; m.Contested = f.contested;
            }
        }
    }
}
