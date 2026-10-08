using Mirror;
using Reflex.Attributes;
using Reflex.Extensions;
using Reflex.Injectors;
using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Cores;
using Tank.Core.Common;
using Tank.Core.Hit;
using Tank.Core.Input;
using Tank.Core.Netcode;
using Tank.Gameplay;
using Tank.Gameplay.Flow;
using Tank.Gameplay.Input;
using UnityEngine;

namespace Tank.Net
{
    /// <summary>
    /// The network presence of one tank (a person or a bot). It carries who the tank is (name, colour), receives the owner's input on the
    /// server and creates the tank in the game on every machine; it holds no rules and draws nothing.
    /// </summary>
    public sealed class NetTank : NetworkBehaviour
    {
        public static NetTank Local { get; private set; }
        static readonly Dictionary<int, NetTank> s_ById = new Dictionary<int, NetTank>();
        public static bool TryGet(int tankId, out NetTank tank) { return s_ById.TryGetValue(tankId, out tank); }

        [SyncVar(hook = nameof(OnName))] public string playerName = "Player";
        [SyncVar] public byte colorSlot;
        [SyncVar] public bool isBot;

        PlayerSpawner m_Spawner; ClientRoster m_Roster; LocalPlayer m_LocalPlayer;
        ITankRegistry m_Tanks; IHitWorld m_World; IClock m_Clock; IRandom m_Random; Tank.Core.Items.IItemQuery m_Items;
        NetworkCommandSource m_Source;
        CoreOfferSystem m_Cores;

        public int TankId => (int)netId;
        public uint AckSeq => m_Source != null ? m_Source.AckSeq : 0;
        public int StarvedTicks => m_Source != null ? m_Source.Starved : 0;

        void Awake()
        {
            // objects Mirror creates at run time are not part of the scene Reflex injected, so each takes its services from the scene container itself
            GameObjectInjector.InjectObject(gameObject, gameObject.scene.GetSceneContainer());
        }

        [Inject]
        void Construct(PlayerSpawner spawner, ClientRoster roster, LocalPlayer local, ITankRegistry tanks, IHitWorld world, IClock clock, IRandom random, CoreOfferSystem cores, Tank.Core.Items.IItemQuery items)
        {
            m_Cores = cores; m_Items = items;
            m_Spawner = spawner; m_Roster = roster; m_LocalPlayer = local; m_Tanks = tanks; m_World = world; m_Clock = clock; m_Random = random;
        }

        /// <summary>Server only, before the object is spawned.</summary>
        public void ServerInit(string name, int slot, bool bot) { playerName = name; colorSlot = (byte)slot; isBot = bot; }

        public override void OnStartServer()
        {
            s_ById[TankId] = this;
            ICommandSource source;
            if (isBot) source = new BotCommandSource(m_Tanks, m_World, m_Clock, m_Random, m_Items);
            else { m_Source = new NetworkCommandSource(); source = m_Source; }
            m_Spawner.Spawn(TankId, playerName, colorSlot, source, false, isBot);
        }

        public override void OnStopServer()
        {
            s_ById.Remove(TankId);
            m_Spawner.Remove(TankId);
        }

        public override void OnStartClient()
        {
            s_ById[TankId] = this;
            if (!isServer) m_Roster.Add(TankId, playerName, colorSlot);      // a host already has the tank from the server side
        }

        public override void OnStopClient()
        {
            if (!isServer) m_Roster.Remove(TankId);
            if (!isServer) s_ById.Remove(TankId);
            if (Local == this) Local = null;
        }

        public override void OnStartLocalPlayer()
        {
            Local = this;
            m_LocalPlayer.TankId = TankId;
            string chosen = string.IsNullOrWhiteSpace(LaunchOptions.PlayerName) ? NetMenuController.ChosenName : LaunchOptions.PlayerName;
            if (!string.IsNullOrWhiteSpace(chosen)) CmdSetName(chosen);       // the server decides the final name; it comes back through the SyncVar
        }

        // a client learns of a new name from the SyncVar; the server renames its own model where it changes it
        void OnName(string oldName, string newName) { if (!isServer && m_Roster != null) m_Roster.Rename(TankId, newName); }

        [Command]
        void CmdSetName(string requested)
        {
            string clean = SanitizeName(requested);
            if (clean.Length == 0 || isBot) return;
            playerName = clean;
            m_Spawner.Rename(TankId, clean);
        }

        static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim()) if (!char.IsControl(c) && sb.Length < 16) sb.Append(c);
            return sb.ToString().Trim();
        }

        /// <summary>Host only: the local player's input goes straight into the same queue a remote player's would.</summary>
        public void EnqueueLocal(InputFrame frame) { if (m_Source != null) m_Source.Enqueue(frame); }

        public void SendInput(InputFrame[] frames) { CmdInput(frames); }
        public void SendCorePick(int offerIndex) { CmdPickCore(offerIndex); }

        [Command]
        void CmdPickCore(int offerIndex) { m_Cores.Pick(TankId, offerIndex); }

        [Command(channel = Channels.Unreliable)]
        void CmdInput(InputFrame[] frames)
        {
            if (m_Source == null || frames == null) return;
            foreach (InputFrame f in frames) m_Source.Enqueue(f);
        }
    }
}
