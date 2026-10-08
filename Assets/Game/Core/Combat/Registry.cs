using System.Collections.Generic;

namespace Tank.Core.Combat
{
    public interface ITankRegistry
    {
        IReadOnlyList<TankModel> All { get; }
        TankModel Find(int id);
    }

    public sealed class TankRegistry : ITankRegistry
    {
        readonly List<TankModel> m_Tanks = new List<TankModel>();
        public IReadOnlyList<TankModel> All => m_Tanks;
        public void Add(TankModel tank) { m_Tanks.Add(tank); }
        public bool Remove(TankModel tank) { return m_Tanks.Remove(tank); }
        public TankModel Find(int id) { for (int i = 0; i < m_Tanks.Count; i++) if (m_Tanks[i].Id == id) return m_Tanks[i]; return null; }
    }
}
