using Tank.Core.Common;
using Tank.Core.Match;

namespace Tank.Core.Combat
{
    /// <summary>Per tick, per tank: drive and aim, then weapons. Movement runs from warmup on, shooting only while the match is on, nothing once it has ended.</summary>
    public sealed class TankStepper : ITickable
    {
        readonly ITankRegistry m_Tanks;
        readonly TankSimulation m_Simulation;
        readonly WeaponSystem m_Weapons;
        readonly IMatchQuery m_Match;

        public TankStepper(ITankRegistry tanks, TankSimulation simulation, WeaponSystem weapons, IMatchQuery match)
        {
            m_Tanks = tanks; m_Simulation = simulation; m_Weapons = weapons; m_Match = match;
        }

        public void Tick(float dt)
        {
            if (m_Match.Phase == MatchPhase.Ended) return;
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel t = m_Tanks.All[i];
                m_Simulation.Step(t, dt);
                if (m_Match.Phase == MatchPhase.Playing) m_Weapons.Step(t, dt);
            }
        }
    }

    /// <summary>Simulation time: advances by the fixed step, read by everything that needs "now" (assist windows, cooldown-free timers).</summary>
    public sealed class SimulationClock : IClock, ITickable
    {
        public float Now { get; private set; }
        public void Tick(float dt) { Now += dt; }
    }
}
