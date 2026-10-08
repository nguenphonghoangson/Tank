using Mirror;
using Tank.Core.Combat;
using Tank.Core.Cores;
using Tank.Core.Common;
using Tank.Core.Input;
using Tank.Core.Match;
using Tank.Core.Netcode;
using Tank.Gameplay;
using Tank.Gameplay.Flow;

namespace Tank.Net
{
    /// <summary>Picks the fixed step for this machine's role: the full rules unless this is a pure client.</summary>
    public sealed class RoleSimulationDriver : ISimulationDriver
    {
        readonly ISessionRole m_Role; readonly LocalSimulationDriver m_Local; readonly ClientSimulationDriver m_Client;
        public RoleSimulationDriver(ISessionRole role, LocalSimulationDriver local, ClientSimulationDriver client) { m_Role = role; m_Local = local; m_Client = client; }
        public void Tick(float dt) { if (m_Role.Current == SessionRole.Client) m_Client.Tick(dt); else m_Local.Tick(dt); }
    }

    /// <summary>A client's pick travels to the server, which owns the offers; everywhere else it goes straight to the rules.</summary>
    public sealed class RoleCorePicker : ICorePicker
    {
        readonly ISessionRole m_Role; readonly CoreOfferSystem m_Local;
        public RoleCorePicker(ISessionRole role, CoreOfferSystem local) { m_Role = role; m_Local = local; }
        public void Pick(int tankId, int offerIndex)
        {
            if (m_Role.Current == SessionRole.Client) { if (NetTank.Local != null) NetTank.Local.SendCorePick(offerIndex); }
            else m_Local.Pick(tankId, offerIndex);
        }
    }

    /// <summary>The match clock and phase: the session itself where the rules run, the synced values on a client.</summary>
    public sealed class RoleMatchQuery : IMatchQuery
    {
        readonly ISessionRole m_Role; readonly MatchSession m_Session;
        public RoleMatchQuery(ISessionRole role, MatchSession session) { m_Role = role; m_Session = session; }
        bool Remote => m_Role.Current == SessionRole.Client;
        public MatchPhase Phase => Remote ? (NetMatch.Instance != null ? NetMatch.Instance.Phase : MatchPhase.Warmup) : m_Session.Phase;
        public float TimeLeft => Remote ? (NetMatch.Instance != null ? NetMatch.Instance.TimeLeft : 0f) : m_Session.TimeLeft;
        public int WinnerId => Remote ? (NetMatch.Instance != null ? NetMatch.Instance.winnerId : -1) : m_Session.WinnerId;
    }

    /// <summary>Last step of the server's tick: send every tank's state to every client.</summary>
    public sealed class ServerSnapshotStep : ITickable
    {
        readonly ISessionRole m_Role;
        uint m_Tick;
        public ServerSnapshotStep(ISessionRole role) { m_Role = role; }

        public void Tick(float dt)
        {
            SessionRole r = m_Role.Current;
            if (r != SessionRole.Server && r != SessionRole.Host) return;
            if (NetMatch.Instance != null) NetMatch.Instance.ServerTick(++m_Tick);
        }
    }

    /// <summary>On a host, the local player's input skips the wire and goes straight into the queue a remote player's would use.</summary>
    public sealed class HostInputFeeder : ITickable
    {
        readonly ISessionRole m_Role; readonly LocalPlayer m_Local; readonly TankRegistry m_Tanks; readonly Tank.Core.Hit.IHitWorld m_World; readonly IClock m_Clock; readonly IRandom m_Random; readonly Tank.Core.Items.IItemQuery m_Items; readonly Tank.Gameplay.Input.PlayerCommandSource m_Player;
        readonly CommandCollector m_Collector = new CommandCollector();
        Tank.Core.Input.ICommandSource m_Source;
        uint m_Seq;

        public HostInputFeeder(ISessionRole role, LocalPlayer local, TankRegistry tanks, Tank.Core.Hit.IHitWorld world, IClock clock, IRandom random, Tank.Core.Items.IItemQuery items, Tank.Gameplay.Input.PlayerCommandSource player)
        {
            m_Player = player; m_Items = items; m_Role = role; m_Local = local; m_Tanks = tanks; m_World = world; m_Clock = clock; m_Random = random;
        }

        public void Tick(float dt)
        {
            if (m_Role.Current != SessionRole.Host || NetTank.Local == null) return;
            TankModel me = m_Tanks.Find(m_Local.TankId);
            if (me == null) return;
            m_Source ??= LocalInputSource.Create(m_Tanks, m_World, m_Clock, m_Random, m_Items, m_Player);
            NetTank.Local.EnqueueLocal(InputFrame.From(++m_Seq, m_Collector.Collect(m_Source, me, me.Intent)));
        }

    }
}
