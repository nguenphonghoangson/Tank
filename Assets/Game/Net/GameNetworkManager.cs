using Mirror;
using Reflex.Attributes;
using Tank.Gameplay.Config;
using Tank.Gameplay.Flow;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tank.Net
{
    /// <summary>Mirror's entry point for this game: starts the match when a server comes up, creates a tank for every player that joins, and returns to the menu when the connection ends.</summary>
    public sealed class GameNetworkManager : NetworkManager
    {
        [Header("Game")]
        [SerializeField] GameObject matchPrefab;
        [SerializeField, Min(0)] int serverBots = 0;

        MatchDirector m_Director; PlayerSpawner m_Spawner; MatchConfig m_Match;

        public override void Awake()
        {
            // test conditions from the command line: wrap the real transport in Mirror's latency simulator and/or change the port
            var kcp = GetComponent<kcp2k.KcpTransport>();
            if (kcp != null && LaunchOptions.Port > 0) kcp.port = (ushort)LaunchOptions.Port;
            if (kcp != null && (LaunchOptions.LatencyMs > 0f || LaunchOptions.LossPercent > 0f))
            {
                // the simulator checks its wrapped transport in Awake, so it is built on an inactive object and woken once it is wired
                var holder = new GameObject("LatencySimulation");
                holder.SetActive(false);
                holder.transform.SetParent(transform, false);
                var lat = holder.AddComponent<LatencySimulation>();
                lat.wrap = kcp; lat.latency = LaunchOptions.LatencyMs; lat.jitter = 0.02f; lat.unreliableLoss = LaunchOptions.LossPercent; lat.unreliableScramble = 0f;
                holder.SetActive(true);
                transport = lat;
            }
            base.Awake();
        }

        /// <summary>When true the server waits in a lobby until StartMatch is called; command-line servers start at once.</summary>
        public bool UseLobby { get; set; }

        public int ServerBots { get => serverBots; set => serverBots = Mathf.Max(0, value); }

        [Inject]
        void Construct(MatchDirector director, PlayerSpawner spawner, MatchConfig match) { m_Director = director; m_Spawner = spawner; m_Match = match; }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkServer.Spawn(Instantiate(matchPrefab));
            if (UseLobby) { NetMatch.Instance.SetLobby(true); return; }
            StartMatch();
        }

        /// <summary>Server only: brings in the bots and starts the match; the lobby closes for everyone.</summary>
        public void StartMatch()
        {
            if (!NetworkServer.active || NetMatch.Instance == null) return;
            for (int i = 0; i < serverBots; i++) SpawnBot(i + 1);
            m_Director.Begin();
            NetMatch.Instance.SetLobby(false);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            GameObject go = Instantiate(playerPrefab);
            go.GetComponent<NetTank>().ServerInit("Player " + conn.connectionId, m_Spawner.FreeSlot(), false);
            NetworkServer.AddPlayerForConnection(conn, go);
        }

        void SpawnBot(int number)
        {
            GameObject go = Instantiate(playerPrefab);
            go.GetComponent<NetTank>().ServerInit("Bot " + number, m_Spawner.FreeSlot(), true);
            NetworkServer.Spawn(go);
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            if (!NetworkServer.active) Restart();                       // the server went away: back to the menu
        }

        /// <summary>Stops whatever is running and reloads the scene, which resets every service to its starting state.</summary>
        public void Leave()
        {
            if (NetworkServer.active && NetworkClient.isConnected) StopHost();
            else if (NetworkServer.active) StopServer();
            else if (NetworkClient.isConnected || NetworkClient.active) StopClient();
            Restart();
        }

        static bool s_Quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetQuit() { s_Quitting = false; }

        // Mirror stops the client from here, and that disconnect must not start loading a scene
        public override void OnApplicationQuit() { s_Quitting = true; base.OnApplicationQuit(); }

        // a disconnect that is part of the process closing must not start loading a scene
        static void Restart() { if (!s_Quitting) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }
    }
}
