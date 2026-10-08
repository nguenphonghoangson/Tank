using System;
using System.Collections.Generic;
using Mirror;
using Reflex.Attributes;
using Reflex.Extensions;
using Reflex.Injectors;
using Tank.Core.Combat;
using Tank.Core.Items;
using Tank.Core.Events;
using Tank.Core.Match;
using Tank.Core.Netcode;
using Tank.Gameplay;
using Tank.Gameplay.Flow;
using UnityEngine;

namespace Tank.Net
{
    /// <summary>
    /// The match as the network sees it, one per game: the server's events and snapshots go out through it, and the clock and phase are
    /// synced from it. On a client it turns what arrives back into the same events the rules would have raised, so the HUD, effects and
    /// views react exactly as they do on the server and know nothing about the network.
    /// </summary>
    public sealed class NetMatch : NetworkBehaviour
    {
        public static NetMatch Instance { get; private set; }

        [SyncVar(hook = nameof(OnPhase))] public byte phase;
        [SyncVar(hook = nameof(OnTime))] public float timeLeft;
        [SyncVar] public int winnerId = -1;
        [SyncVar(hook = nameof(OnLobby))] public bool lobby;

        IEventBus m_Bus; IMatchQuery m_Server; ITankRegistry m_Tanks; IStatsLedger m_Ledger; IRankingQuery m_Ranking; IItemQuery m_Items; ProjectileTracker m_Tracker; ClientSimulationDriver m_Client; LobbyState m_Lobby;
        readonly List<IDisposable> m_Subscriptions = new List<IDisposable>();
        float m_LocalTime;

        public MatchPhase Phase => (MatchPhase)phase;
        public float TimeLeft => m_LocalTime;

        void Awake()
        {
            syncInterval = 0.2f;                  // the clock and phase need not travel every tick; the client counts down between updates
            GameObjectInjector.InjectObject(gameObject, gameObject.scene.GetSceneContainer());
        }

        [Inject]
        void Construct(IEventBus bus, MatchSession session, ITankRegistry tanks, IStatsLedger ledger, IRankingQuery ranking, ProjectileTracker tracker, ClientSimulationDriver client, IItemQuery items, LobbyState lobbyState)
        {
            m_Items = items; m_Lobby = lobbyState;
            m_Bus = bus; m_Server = session; m_Tanks = tanks; m_Ledger = ledger; m_Ranking = ranking; m_Tracker = tracker; m_Client = client;
        }

        // ------------------------------------------------------------------ server: what happens goes out

        public override void OnStartServer()
        {
            Instance = this;
            m_Subscriptions.Add(m_Bus.Subscribe<ProjectileFired>(e => RpcFired(e.ProjectileId, e.OwnerId, e.WeaponId, e.Origin, e.Direction)));
            m_Subscriptions.Add(m_Bus.Subscribe<ProjectileImpact>(e => RpcImpact(e.ProjectileId, e.WeaponId, e.Point, e.HitTank)));
            m_Subscriptions.Add(m_Bus.Subscribe<TankDamaged>(e => RpcDamaged(e.VictimId, e.AttackerId, e.Amount, e.HpLeft)));
            m_Subscriptions.Add(m_Bus.Subscribe<TankKilled>(e => RpcKilled(e.VictimId, e.KillerId, e.AssistIds ?? new int[0])));
            m_Subscriptions.Add(m_Bus.Subscribe<TankDashed>(e => RpcDashed(e.TankId, e.Position, e.Direction)));
            m_Subscriptions.Add(m_Bus.Subscribe<TankRespawned>(OnServerRespawned));
            m_Subscriptions.Add(m_Bus.Subscribe<ItemSlotChanged>(e => RpcItemSlot(e.SlotId, e.ItemId, e.Position)));
            m_Subscriptions.Add(m_Bus.Subscribe<ItemTaken>(e => RpcItemTaken(e.SlotId, e.TankId, e.ItemId, e.Position)));
            m_Subscriptions.Add(m_Bus.Subscribe<CorePicked>(e => RpcCorePicked(e.TankId, e.CoreId)));
            m_Subscriptions.Add(m_Bus.Subscribe<CoreOffered>(OnServerOffered));
            m_Subscriptions.Add(m_Bus.Subscribe<MatchPhaseChanged>(e => { phase = (byte)e.Phase; RpcPhase((byte)e.Phase); }));
            m_Subscriptions.Add(m_Bus.Subscribe<MatchEnded>(e => { winnerId = e.WinnerId; RpcEnded(e.WinnerId); }));
        }

        public override void OnStopServer()
        {
            foreach (IDisposable s in m_Subscriptions) s.Dispose();
            m_Subscriptions.Clear();
            if (Instance == this) Instance = null;
        }

        /// <summary>Called once per tick by the server pipeline.</summary>
        public void ServerTick(uint tick)
        {
            timeLeft = m_Server.TimeLeft;
            m_LocalTime = timeLeft;
            IReadOnlyList<TankModel> tanks = m_Tanks.All;
            var snaps = new TankSnapshot[tanks.Count];
            for (int i = 0; i < tanks.Count; i++)
            {
                NetTank.TryGet(tanks[i].Id, out NetTank nt);
                snaps[i] = TankSnapshot.Capture(tanks[i], nt != null ? nt.AckSeq : 0u);
            }
            RpcSnapshot(tick, snaps);
        }

        /// <summary>An offer is private: only the tank's owner is told what the choices are.</summary>
        void OnServerOffered(CoreOffered e)
        {
            if (NetTank.TryGet(e.TankId, out NetTank tank) && tank.connectionToClient != null) TargetOffered(tank.connectionToClient, e.TankId, e.CoreIds, e.Seconds);
        }

        void OnServerRespawned(TankRespawned e)
        {
            TankModel t = m_Tanks.Find(e.TankId);
            if (t != null) RpcRespawned(e.TankId, t.Position, t.Body.Yaw);
        }

        // ------------------------------------------------------------------ client: it comes back in

        public override void OnStartClient()
        {
            Instance = this;
            m_LocalTime = timeLeft;
            if (!isServer) { m_Lobby.Active = lobby; CmdRequestState(); }
        }

        public override void OnStopClient() { if (Instance == this && !isServer) Instance = null; }

        void Update() { if (!isServer && Phase != MatchPhase.Ended) m_LocalTime = Mathf.Max(0f, m_LocalTime - Time.unscaledDeltaTime); }

        /// <summary>Server only: opens or closes the lobby for everyone.</summary>
        public void SetLobby(bool open) { lobby = open; m_Lobby.Active = open; }

        void OnLobby(bool oldValue, bool newValue) { m_Lobby.Active = newValue; }

        void OnPhase(byte oldValue, byte newValue) { if (!isServer) m_LocalTime = timeLeft; }
        void OnTime(float oldValue, float newValue) { m_LocalTime = newValue; }

        [Command(requiresAuthority = false)]
        void CmdRequestState(NetworkConnectionToClient sender = null)
        {
            var ids = new List<int>(); var k = new List<int>(); var d = new List<int>(); var a = new List<int>();
            foreach (PlayerStats s in m_Ranking.Ranked) { ids.Add(s.Id); k.Add(s.Kills); d.Add(s.Deaths); a.Add(s.Assists); }
            TargetState(sender, ids.ToArray(), k.ToArray(), d.ToArray(), a.ToArray());

            // the items already lying on the map
            var slotIds = new List<int>(); var itemIds = new List<int>(); var positions = new List<Vector2>();
            foreach (ItemSlotState s in m_Items.Slots) { slotIds.Add(s.SlotId); itemIds.Add(s.ItemId); positions.Add(s.Position); }
            TargetItems(sender, slotIds.ToArray(), itemIds.ToArray(), positions.ToArray());
        }

        [TargetRpc]
        void TargetState(NetworkConnectionToClient target, int[] ids, int[] kills, int[] deaths, int[] assists)
        {
            for (int i = 0; i < ids.Length; i++) m_Ledger.Set(ids[i], kills[i], deaths[i], assists[i]);
        }

        [TargetRpc]
        void TargetItems(NetworkConnectionToClient target, int[] slotIds, int[] itemIds, Vector2[] positions)
        {
            for (int i = 0; i < slotIds.Length; i++) m_Bus.Publish(new ItemSlotChanged(slotIds[i], itemIds[i], positions[i]));
        }

        [TargetRpc]
        void TargetOffered(NetworkConnectionToClient target, int tankId, int[] coreIds, float seconds)
        {
            if (!isServer) m_Bus.Publish(new CoreOffered(tankId, coreIds, seconds));
        }

        [ClientRpc] void RpcItemSlot(int slot, int item, Vector2 position) { if (!isServer) m_Bus.Publish(new ItemSlotChanged(slot, item, position)); }
        [ClientRpc] void RpcItemTaken(int slot, int tank, int item, Vector2 position) { if (!isServer) m_Bus.Publish(new ItemTaken(slot, tank, item, position)); }
        [ClientRpc] void RpcCorePicked(int tank, int core) { if (!isServer) m_Bus.Publish(new CorePicked(tank, core)); }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcSnapshot(uint tick, TankSnapshot[] snapshots) { if (!isServer) m_Client.OnSnapshot(tick, snapshots); }

        [ClientRpc] void RpcFired(int id, int owner, int weapon, Vector2 origin, Vector2 dir) { if (!isServer) m_Bus.Publish(new ProjectileFired(id, owner, weapon, origin, dir)); }
        [ClientRpc] void RpcImpact(int id, int weapon, Vector2 point, bool hitTank) { if (!isServer) m_Bus.Publish(new ProjectileImpact(id, weapon, point, hitTank)); }
        [ClientRpc] void RpcDamaged(int victim, int attacker, int amount, int hpLeft) { if (!isServer) m_Bus.Publish(new TankDamaged(victim, attacker, amount, hpLeft)); }
        [ClientRpc] void RpcKilled(int victim, int killer, int[] assists) { if (!isServer) m_Bus.Publish(new TankKilled(victim, killer, assists)); }
        [ClientRpc] void RpcDashed(int id, Vector2 pos, Vector2 dir) { if (!isServer) m_Bus.Publish(new TankDashed(id, pos, dir)); }

        [ClientRpc]
        void RpcRespawned(int id, Vector2 position, float yaw)
        {
            if (isServer) return;
            m_Client.OnRespawned(id, position, yaw);
            m_Bus.Publish(new TankRespawned(id));
        }

        [ClientRpc]
        void RpcPhase(byte newPhase)
        {
            if (isServer) return;
            if ((MatchPhase)newPhase == MatchPhase.Warmup) { m_Ledger.Reset(); m_Tracker.Clear(); }      // a new match: scores and shots in the air start over
            m_Bus.Publish(new MatchPhaseChanged((MatchPhase)newPhase));
        }

        [ClientRpc] void RpcEnded(int winner) { if (!isServer) m_Bus.Publish(new MatchEnded(winner)); }
    }
}
