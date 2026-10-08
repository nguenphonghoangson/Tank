using System;
using Mirror;
using Reflex.Attributes;
using Tank.Gameplay;
using Tank.Gameplay.Flow;
using Tank.Core.Events;
using Tank.Core.Match;
using UnityEngine;

namespace Tank.Net
{
    /// <summary>
    /// What the main menu and the session bar act on: play offline, host, or join, and leave. The command line can make the same choice with nobody at the keyboard
    /// (-host, -server, -client -address X, -offline), which is how dedicated servers and test clients start.
    /// </summary>
    public sealed class NetMenuController : MonoBehaviour
    {
        [SerializeField] GameBootstrap bootstrap;
        [SerializeField] GameNetworkManager manager;
        [SerializeField] RoomDiscovery discovery;
        [SerializeField] string defaultAddress = "localhost";       // what the Join box starts with; the command line's -address wins

        ISessionRole m_Role; ClientSimulationDriver m_Client; IRankingQuery m_Ranking; IMatchQuery m_Match; Tank.Core.Combat.ITankRegistry m_Tanks; Tank.Core.Items.IItemQuery m_ItemQuery;
        int m_Fired, m_Damaged, m_Killed, m_Impacts, m_ItemsTaken, m_CoresPicked;
        bool m_Started;
        string m_Address = "localhost", m_Bots = "2", m_Name;
        public bool Started => m_Started;
        public string Address => m_Address;
        public string BotCount => m_Bots;
        public string PlayerName => m_Name;
        public ISessionRole Role => m_Role;
        public bool IsServer => m_Role != null && (m_Role.Current == SessionRole.Host || m_Role.Current == SessionRole.Server);
        public static string ChosenName { get; private set; } = string.Empty;
        float m_NextLog, m_QuitAt, m_LobbyStartAt;
        Tank.Gameplay.Flow.LobbyState m_LobbyState;

        [Inject]
        void Construct(ISessionRole role, ClientSimulationDriver client, IRankingQuery ranking, IMatchQuery match, IEventBus bus, Tank.Core.Combat.ITankRegistry tanks, Tank.Core.Items.IItemQuery itemQuery, Tank.Gameplay.Flow.LobbyState lobbyState)
        {
            m_LobbyState = lobbyState;
            m_ItemQuery = itemQuery; m_Tanks = tanks;
            m_Role = role; m_Client = client; m_Ranking = ranking; m_Match = match;
            bus.Subscribe<ProjectileFired>(e => m_Fired++);
            bus.Subscribe<ProjectileImpact>(e => m_Impacts++);
            bus.Subscribe<TankDamaged>(e => m_Damaged++);
            bus.Subscribe<TankKilled>(e => m_Killed++);
            bus.Subscribe<Tank.Core.Events.ItemTaken>(e => m_ItemsTaken++);
            bus.Subscribe<CorePicked>(e => m_CoresPicked++);
        }

        void Start()
        {
            if (discovery != null) discovery.Describe = DescribeRoom;
            m_Address = LaunchOptions.AddressGiven ? LaunchOptions.Address : defaultAddress;
            m_Name = !string.IsNullOrEmpty(LaunchOptions.PlayerName) ? LaunchOptions.PlayerName : "Player" + UnityEngine.Random.Range(100, 999);
            ChosenName = m_Name;
            if (LaunchOptions.Bots >= 0) m_Bots = LaunchOptions.Bots.ToString();
            if (LaunchOptions.QuitAfterSeconds > 0f) m_QuitAt = Time.realtimeSinceStartup + LaunchOptions.QuitAfterSeconds;
            switch (LaunchOptions.Start)
            {
                case LaunchOptions.Mode.Offline: Offline(); break;
                case LaunchOptions.Mode.Host: Host(); break;
                case LaunchOptions.Mode.Server: Server(); break;
                case LaunchOptions.Mode.Client: Join(); break;
            }
        }

        public void Configure(string name, string address, string bots)
        {
            if (!string.IsNullOrWhiteSpace(name)) m_Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(address)) m_Address = address.Trim();
            m_Bots = string.IsNullOrWhiteSpace(bots) ? "0" : bots.Trim();
        }
        /// <summary>Joins a room picked from the list.</summary>
        public void JoinAddress(string address) { m_Address = address; Join(); }

        public void Leave() { manager.Leave(); }

        public void Offline() { m_Started = true; ChosenName = m_Name; bootstrap.LocalPlayerName = m_Name; bootstrap.StartOffline(Bots()); }
        public void Host() { m_Started = true; ChosenName = m_Name; manager.ServerBots = Bots(); manager.UseLobby = LaunchOptions.Start == LaunchOptions.Mode.Menu || LaunchOptions.Lobby; manager.StartHost(); Advertise(); }
        void Advertise() { if (discovery == null) return; try { discovery.AdvertiseServer(); } catch (System.Exception e) { Debug.LogWarning("Room advertising unavailable: " + e.Message); } }

        public void StartMatch(int bots) { manager.ServerBots = bots; manager.StartMatch(); }
        public void Server() { m_Started = true; manager.ServerBots = Bots(); manager.UseLobby = LaunchOptions.Start == LaunchOptions.Mode.Menu || LaunchOptions.Lobby; m_LobbyStartAt = LaunchOptions.StartAfterSeconds > 0f ? Time.realtimeSinceStartup + LaunchOptions.StartAfterSeconds : 0f; manager.StartServer(); Advertise(); }
        public void Join() { m_Started = true; ChosenName = m_Name; manager.networkAddress = m_Address; manager.StartClient(); }
        int Bots() { return int.TryParse(m_Bots, out int n) ? Mathf.Clamp(n, 0, 7) : 0; }

        void Update()
        {
            if (m_LobbyStartAt > 0f && Time.realtimeSinceStartup >= m_LobbyStartAt && m_LobbyState.Active) { m_LobbyStartAt = 0f; StartMatch(Bots()); }
            if (m_QuitAt > 0f && Time.realtimeSinceStartup >= m_QuitAt) { LogStats(); Application.Quit(); return; }
            if (LaunchOptions.LogStats && m_Started && Time.realtimeSinceStartup >= m_NextLog) { m_NextLog = Time.realtimeSinceStartup + 5f; LogStats(); }
        }

        RoomInfo DescribeRoom()
        {
            int humans = 0;
            foreach (var t in m_Tanks.All) if (!t.IsBot) humans++;
            return new RoomInfo { hostName = m_Name, players = humans, maxPlayers = manager.maxConnections, inLobby = m_LobbyState.Active };
        }

        int FilledSlots() { int n = 0; foreach (var s in m_ItemQuery.Slots) if (s.ItemId >= 0) n++; return n; }

        void LogStats()
        {
            string line = "[net] role=" + m_Role.Current + " lobby=" + m_LobbyState.Active + " phase=" + m_Match.Phase + " t=" + Mathf.RoundToInt(m_Match.TimeLeft) + " fired=" + m_Fired + " impacts=" + m_Impacts + " damaged=" + m_Damaged + " killed=" + m_Killed + " slotsWithItem=" + FilledSlots() + "/" + m_ItemQuery.Slots.Count + " items=" + m_ItemsTaken + " cores=" + m_CoresPicked + " tanks=" + (m_Ranking != null ? m_Ranking.Ranked.Count : 0);
            if (m_Role.Current == SessionRole.Client) line += " rtt=" + Mathf.RoundToInt((float)(NetworkTime.rtt * 1000.0)) + "ms " + m_Client.StatsLine();
            if (m_Ranking != null) foreach (PlayerStats s in m_Ranking.Ranked) line += " | #" + s.Id + (m_Tanks.Find(s.Id) != null ? "(" + m_Tanks.Find(s.Id).Name + ")" : "") + " K" + s.Kills + " D" + s.Deaths + " A" + s.Assists;
            Debug.Log(line);
        }
    }
}
