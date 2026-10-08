using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Core.Input;
using Tank.Core.Match;
using Tank.Gameplay.Config;

namespace Tank.Gameplay.Flow
{
    /// <summary>Creates a tank for a player or a bot: picks a free colour slot, registers it everywhere it needs to be known, and puts it in the arena.</summary>
    public sealed class PlayerSpawner
    {
        readonly TankRegistry m_Registry;
        readonly TankLifecycle m_Lifecycle;
        readonly InputProcessor m_Input;
        readonly IStatsLedger m_Ledger;
        readonly TankConfig m_Config;
        readonly PlayerPalette m_Palette;
        readonly LocalPlayer m_Local;
        readonly IEventBus m_Bus;
        int m_NextId = 1;

        public PlayerSpawner(TankRegistry registry, TankLifecycle lifecycle, InputProcessor input, IStatsLedger ledger, TankConfig config, PlayerPalette palette, LocalPlayer local, IEventBus bus)
        {
            m_Bus = bus; m_Registry = registry; m_Lifecycle = lifecycle; m_Input = input; m_Ledger = ledger; m_Config = config; m_Palette = palette; m_Local = local;
        }

        public TankModel Spawn(string name, ICommandSource source, bool isLocal, bool isBot = false) { return Spawn(m_NextId++, name, FreeSlot(), source, isLocal, isBot); }

        /// <summary>For networked games, where the network decides the id and the server the colour.</summary>
        public TankModel Spawn(int id, string name, int slot, ICommandSource source, bool isLocal, bool isBot = false)
        {
            var tank = new TankModel(id, name, slot, m_Config.maxHp) { IsBot = isBot };
            m_Registry.Add(tank);
            m_Ledger.Register(tank.Id);
            m_Input.Bind(tank.Id, source);
            if (isLocal) m_Local.TankId = tank.Id;
            m_Lifecycle.Spawn(tank);
            return tank;
        }

        public void Rename(int id, string name)
        {
            TankModel t = m_Registry.Find(id);
            if (t == null) return;
            t.Name = name;
            m_Bus.Publish(new TankRenamed(id, name));
        }

        public void Remove(int id)
        {
            TankModel t = m_Registry.Find(id);
            if (t == null) return;
            m_Registry.Remove(t);
            m_Input.Unbind(id);
            m_Ledger.Unregister(id);
            m_Bus.Publish(new TankRemoved(id));
        }

        public int FreeSlot()
        {
            for (int slot = 0; slot < m_Palette.Count; slot++)
            {
                bool used = false;
                for (int i = 0; i < m_Registry.All.Count; i++) if (m_Registry.All[i].ColorSlot == slot) { used = true; break; }
                if (!used) return slot;
            }
            return m_Registry.All.Count % System.Math.Max(1, m_Palette.Count);       // more players than colours: colours repeat
        }
    }
}
