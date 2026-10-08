using System;
using System.Collections.Generic;
using Tank.Core.Events;
using UnityEngine;

namespace Tank.Core.Match
{
    public sealed class PlayerStats
    {
        public readonly int Id;
        public int Kills, Deaths, Assists;
        public PlayerStats(int id) { Id = id; }
        /// <summary>(kills + assists) per death, deaths floored at 1 so a clean run is not infinite.</summary>
        public float Kda => (Kills + Assists) / (float)Mathf.Max(1, Deaths);
    }

    /// <summary>Strategy: how two players are ordered. Swap it to rank by score, kills or anything else without touching the ledger.</summary>
    public interface IRankingPolicy
    {
        /// <returns>negative when a ranks above b.</returns>
        int Compare(PlayerStats a, PlayerStats b);
    }

    /// <summary>Higher KDA first; ties go to more kills, then fewer deaths.</summary>
    public sealed class KdaRankingPolicy : IRankingPolicy
    {
        public int Compare(PlayerStats a, PlayerStats b)
        {
            int c = b.Kda.CompareTo(a.Kda);
            if (c == 0) c = b.Kills.CompareTo(a.Kills);
            if (c == 0) c = a.Deaths.CompareTo(b.Deaths);
            return c;
        }
    }

    public interface IRankingQuery
    {
        IReadOnlyList<PlayerStats> Ranked { get; }
        PlayerStats Get(int id);
        int RankOf(int id);                              // 1-based, 0 when unknown
        /// <summary>false when nobody has scored or the top places are tied on everything the policy looks at.</summary>
        bool TryGetWinner(out int id);
    }

    public interface IStatsLedger
    {
        void Register(int id);
        void Unregister(int id);
        void Set(int id, int kills, int deaths, int assists);     // a joining client takes the server's table
        void Reset();
    }

    /// <summary>Counts kills, deaths and assists from the damage events and keeps the table ordered by the ranking policy.</summary>
    public sealed class KdaLedger : IRankingQuery, IStatsLedger, IDisposable
    {
        readonly IRankingPolicy m_Policy;
        readonly IEventBus m_Bus;
        readonly Dictionary<int, PlayerStats> m_ById = new Dictionary<int, PlayerStats>();
        readonly List<PlayerStats> m_Ranked = new List<PlayerStats>();
        readonly IDisposable m_Subscription;

        public KdaLedger(IRankingPolicy policy, IEventBus bus)
        {
            m_Policy = policy; m_Bus = bus;
            m_Subscription = bus.Subscribe<TankKilled>(OnKilled);
        }

        public IReadOnlyList<PlayerStats> Ranked => m_Ranked;
        public PlayerStats Get(int id) { m_ById.TryGetValue(id, out PlayerStats s); return s; }

        public int RankOf(int id)
        {
            for (int i = 0; i < m_Ranked.Count; i++) if (m_Ranked[i].Id == id) return i + 1;
            return 0;
        }

        public void Register(int id)
        {
            if (m_ById.ContainsKey(id)) return;
            var s = new PlayerStats(id);
            m_ById[id] = s; m_Ranked.Add(s);
            Resort();
        }

        public void Unregister(int id)
        {
            if (!m_ById.TryGetValue(id, out PlayerStats s)) return;
            m_ById.Remove(id); m_Ranked.Remove(s);
            m_Bus.Publish(new RankingChanged());
        }

        public void Set(int id, int kills, int deaths, int assists)
        {
            Register(id);
            PlayerStats s = m_ById[id];
            s.Kills = kills; s.Deaths = deaths; s.Assists = assists;
            Resort();
        }

        public void Reset()
        {
            foreach (PlayerStats s in m_ById.Values) { s.Kills = 0; s.Deaths = 0; s.Assists = 0; }
            Resort();
        }

        public bool TryGetWinner(out int id)
        {
            id = -1;
            if (m_Ranked.Count == 0) return false;
            PlayerStats top = m_Ranked[0];
            if (top.Kda <= 0f) return false;
            if (m_Ranked.Count > 1 && m_Policy.Compare(top, m_Ranked[1]) == 0) return false;
            id = top.Id;
            return true;
        }

        void OnKilled(TankKilled e)
        {
            PlayerStats victim = Get(e.VictimId);
            if (victim != null) victim.Deaths++;
            if (e.KillerId != e.VictimId) { PlayerStats killer = Get(e.KillerId); if (killer != null) killer.Kills++; }
            if (e.AssistIds != null) foreach (int a in e.AssistIds) { PlayerStats s = Get(a); if (s != null) s.Assists++; }
            Resort();
        }

        void Resort()
        {
            // insertion sort keeps equal players in the order they joined, so the table does not shuffle on ties
            for (int i = 1; i < m_Ranked.Count; i++)
            {
                PlayerStats x = m_Ranked[i]; int j = i - 1;
                while (j >= 0 && m_Policy.Compare(x, m_Ranked[j]) < 0) { m_Ranked[j + 1] = m_Ranked[j]; j--; }
                m_Ranked[j + 1] = x;
            }
            m_Bus.Publish(new RankingChanged());
        }

        public void Dispose() { m_Subscription.Dispose(); }
    }
}
