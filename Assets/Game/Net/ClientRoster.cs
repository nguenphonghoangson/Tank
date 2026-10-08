using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Core.Match;
using Tank.Gameplay.Config;

namespace Tank.Net
{
    /// <summary>
    /// The tanks a client knows about but does not simulate. A tank appears in the game on its first snapshot, so it is created where it
    /// really is (and its spawn effect plays there), not at the origin.
    /// </summary>
    public sealed class ClientRoster
    {
        readonly TankRegistry m_Registry;
        readonly IStatsLedger m_Ledger;
        readonly IEventBus m_Bus;
        readonly TankConfig m_Config;
        readonly Dictionary<int, TankModel> m_Pending = new Dictionary<int, TankModel>();

        public ClientRoster(TankRegistry registry, IStatsLedger ledger, IEventBus bus, TankConfig config)
        {
            m_Registry = registry; m_Ledger = ledger; m_Bus = bus; m_Config = config;
        }

        public void Add(int id, string name, int slot)
        {
            if (m_Registry.Find(id) != null || m_Pending.ContainsKey(id)) return;
            m_Pending[id] = new TankModel(id, name, slot, m_Config.maxHp);
            m_Ledger.Register(id);
        }

        /// <summary>Called with each snapshot entry; promotes a waiting tank into the game, already standing where the server has it.</summary>
        public TankModel Resolve(in Tank.Core.Netcode.TankSnapshot snapshot, bool announce)
        {
            TankModel t = m_Registry.Find(snapshot.Id);
            if (t != null) return t;
            if (!m_Pending.TryGetValue(snapshot.Id, out t)) return null;
            m_Pending.Remove(snapshot.Id);
            snapshot.ApplyMotion(t);
            t.Health.Set(snapshot.Hp);
            m_Registry.Add(t);
            if (announce) m_Bus.Publish(new TankSpawned(snapshot.Id));
            return t;
        }

        /// <summary>The server sent a new name: rename the tank whether it is already in the game or still waiting for its first snapshot.</summary>
        public void Rename(int id, string name)
        {
            if (m_Pending.TryGetValue(id, out TankModel pending)) { pending.Name = name; return; }
            TankModel t = m_Registry.Find(id);
            if (t == null) return;
            t.Name = name;
            m_Bus.Publish(new TankRenamed(id, name));
        }

        public void Remove(int id)
        {
            m_Pending.Remove(id);
            TankModel t = m_Registry.Find(id);
            if (t == null) { m_Ledger.Unregister(id); return; }
            m_Registry.Remove(t);
            m_Ledger.Unregister(id);
            m_Bus.Publish(new TankRemoved(id));
        }
    }
}
