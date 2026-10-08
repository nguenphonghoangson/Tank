using System.Collections.Generic;
using Mirror;
using TankGame.Prototype;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>State of one flag as sent to clients (hold and progress quantised to 0..255).</summary>
    public struct FlagSync { public sbyte owner, capturer; public byte hold, progress; public bool contested; }

    /// <summary>Kills, deaths and assists of one player, keyed by the tank's netId.</summary>
    public struct KdaSync { public uint id; public ushort k, d, a; }

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
        public const float AssistSeconds = 10f;            // damage dealt this recently before a kill counts as an assist

        /// <summary>(kills + assists) per death, with deaths floored at 1 so a clean run is not infinite.</summary>
        public static float Ratio(int k, int d, int a) { return (k + a) / (float)Mathf.Max(1, d); }
        public bool TryGetKda(uint id, out KdaSync v)
        {
            for (int i = 0; i < kda.Count; i++) if (kda[i].id == id) { v = kda[i]; return true; }
            v = new KdaSync { id = id }; return false;
        }

        public readonly SyncList<FlagSync> flags = new SyncList<FlagSync>();
        public readonly SyncList<KdaSync> kda = new SyncList<KdaSync>();   // only players that have a kill, death or assist yet
        [SyncVar] public int score0, score1;
        [SyncVar] public int timeLeft = (int)MatchSeconds;
        [SyncVar] public int winner = -2;                  // -2 running, -1 draw, otherwise the team of the winning player
        [SyncVar] public uint winnerId;                    // netId of the winning player (0 = none); the match is decided by highest KDA

        public const float PhaseSeconds = MatchSeconds / SpikeCores.Phases;   // a new core offer at the start of each phase
        public int Phase { get; private set; } = -1;                           // server: 0..3 while running, -1 before the first tick
        readonly float[] m_Power = new float[2];

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

        void Bump(uint id, int dk, int dd, int da)
        {
            for (int i = 0; i < kda.Count; i++)
                if (kda[i].id == id) { KdaSync v = kda[i]; v.k = (ushort)(v.k + dk); v.d = (ushort)(v.d + dd); v.a = (ushort)(v.a + da); kda[i] = v; return; }
            kda.Add(new KdaSync { id = id, k = (ushort)dk, d = (ushort)dd, a = (ushort)da });
        }

        /// <summary>A tank died. The victim always takes a death; the killer a kill only against the other team; assisters are those who damaged it recently.</summary>
        public void ServerKill(SpikeTank shooter, SpikeTank victim, List<uint> assisters)
        {
            if (winner != -2) return;
            Bump(victim.netId, 0, 1, 0);
            int a = TeamOf(shooter.netId), b = TeamOf(victim.netId);
            if (a == b) return;
            ServerAdd(a, Mathf.RoundToInt(KillPoints * SpikeCores.Mods(shooter.ServerState.cores).killScoreMult));
            Bump(shooter.netId, 1, 0, 0);
            if (assisters != null) foreach (uint id in assisters) Bump(id, 0, 0, 1);
        }

        /// <summary>Highest KDA wins; ties fall to more kills, then fewer deaths; a tie on all three, or nobody scoring, is a draw.</summary>
        void ServerDecide()
        {
            uint best = 0; float bestR = -1f; int bestK = -1, bestD = int.MaxValue; bool tie = false;
            foreach (SpikeTank t in SpikeServer.I.Tanks)
            {
                TryGetKda(t.netId, out KdaSync v);
                float r = Ratio(v.k, v.d, v.a);
                int cmp = r.CompareTo(bestR); if (cmp == 0) cmp = v.k.CompareTo(bestK); if (cmp == 0) cmp = bestD.CompareTo((int)v.d);
                if (cmp > 0) { best = t.netId; bestR = r; bestK = v.k; bestD = v.d; tie = false; }
                else if (cmp == 0) tie = true;
            }
            if (best == 0 || tie || bestR <= 0f) { winner = -1; winnerId = 0; }
            else { winner = TeamOf(best); winnerId = best; }
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
            int phase = Mathf.Clamp((int)((MatchSeconds - m_TimeLeft) / PhaseSeconds), 0, SpikeCores.Phases - 1);
            if (phase != Phase) { Phase = phase; foreach (SpikeTank t in SpikeServer.I.Tanks) t.ServerOwePick(); }
            for (int i = 0; i < Points.Length; i++)
            {
                ControlPoint p = Points[i];
                m_Counts[0] = m_Counts[1] = 0; m_Power[0] = m_Power[1] = 0f;
                foreach (SpikeTank t in SpikeServer.I.Tanks)
                    if (!t.ServerDead && p.Contains(t.ServerState.pos)) { int tm = TeamOf(t.netId); m_Counts[tm]++; m_Power[tm] += SpikeCores.Mods(t.ServerState.cores).captureMult; }
                CapturePointModel.Change ch = p.Model.Tick(dt, m_Counts, m_Power, out int team);
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
                ServerDecide();
                m_EndTimer = RestartAfter;
            }
        }

        void ServerRestart()
        {
            score0 = score1 = 0; m_Income[0] = m_Income[1] = 0f;
            kda.Clear(); winnerId = 0; Phase = -1;
            foreach (SpikeTank t in SpikeServer.I.Tanks) t.ServerResetCores();
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
