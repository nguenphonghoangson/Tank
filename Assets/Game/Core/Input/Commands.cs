using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Common;
using UnityEngine;

namespace Tank.Core.Input
{
    /// <summary>One thing a player wants to do. Keyboard, touch, bot and network each produce commands; the simulation only ever sees the result.</summary>
    public interface ITankCommand { void Apply(ref TankIntent intent); }

    public sealed class MoveCommand : ITankCommand
    {
        public Vector2 Direction;
        public void Apply(ref TankIntent intent) { intent.Move = Direction; }
    }

    public sealed class AimCommand : ITankCommand
    {
        public float Yaw;
        public void Apply(ref TankIntent intent) { intent.AimYaw = Yaw; }
    }

    public sealed class FireCommand : ITankCommand
    {
        public void Apply(ref TankIntent intent) { intent.Fire = true; }
    }

    public sealed class DashCommand : ITankCommand
    {
        public void Apply(ref TankIntent intent) { intent.Dash = true; }
    }

    public interface ICommandSource
    {
        /// <summary>Adds this tick's commands for the tank. Sources reuse their command objects, so a tick allocates nothing.</summary>
        void Collect(TankModel tank, List<ITankCommand> into);
    }

    /// <summary>Turns one source's commands into a TankIntent. The server's input processor and a predicting client use the same code, so they cannot disagree on what a key press means.</summary>
    public sealed class CommandCollector
    {
        readonly List<ITankCommand> m_Commands = new List<ITankCommand>(8);

        public TankIntent Collect(ICommandSource source, TankModel tank, TankIntent previous)
        {
            TankIntent intent = previous;
            intent.Move = Vector2.zero; intent.Fire = false; intent.Dash = false;          // aim persists, everything else is "held this tick"
            m_Commands.Clear();
            source.Collect(tank, m_Commands);
            for (int c = 0; c < m_Commands.Count; c++) m_Commands[c].Apply(ref intent);
            return intent;
        }
    }

    /// <summary>Asks each tank's command source what it wants and writes the outcome to the tank's intent, before the simulation steps.</summary>
    public sealed class InputProcessor : ITickable
    {
        readonly ITankRegistry m_Tanks;
        readonly Dictionary<int, ICommandSource> m_Sources = new Dictionary<int, ICommandSource>();
        readonly CommandCollector m_Collector = new CommandCollector();

        public InputProcessor(ITankRegistry tanks) { m_Tanks = tanks; }
        public void Bind(int tankId, ICommandSource source) { m_Sources[tankId] = source; }
        public void Unbind(int tankId) { m_Sources.Remove(tankId); }

        public void Tick(float dt)
        {
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel tank = m_Tanks.All[i];
                if (!m_Sources.TryGetValue(tank.Id, out ICommandSource source)) continue;
                tank.Intent = m_Collector.Collect(source, tank, tank.Intent);
            }
        }
    }
}
