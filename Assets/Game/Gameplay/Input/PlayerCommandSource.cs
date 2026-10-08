using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Input;

namespace Tank.Gameplay.Input
{
    /// <summary>
    /// Whatever the person at this device is using: keyboard and mouse, or touch. Both are asked each tick; the touch controls only add
    /// commands while a finger is down, so a laptop with a touchscreen works either way.
    /// </summary>
    public sealed class PlayerCommandSource : ICommandSource
    {
        readonly KeyboardMouseCommandSource m_Keyboard = new KeyboardMouseCommandSource();
        readonly TouchCommandSource m_Touch;

        public PlayerCommandSource(TouchControls touch) { m_Touch = new TouchCommandSource(touch); }

        public void Collect(TankModel tank, List<ITankCommand> into)
        {
            m_Keyboard.Collect(tank, into);
            m_Touch.Collect(tank, into);          // later commands win, so a touch overrides the keyboard while it is in use
        }
    }

    /// <summary>Turns the touch controls' state into the same commands a keyboard would give.</summary>
    public sealed class TouchCommandSource : ICommandSource
    {
        readonly TouchControls m_Controls;
        readonly MoveCommand m_Move = new MoveCommand();
        readonly AimCommand m_Aim = new AimCommand();
        readonly FireCommand m_Fire = new FireCommand();
        readonly DashCommand m_Dash = new DashCommand();

        public TouchCommandSource(TouchControls controls) { m_Controls = controls; }

        public void Collect(TankModel tank, List<ITankCommand> into)
        {
            if (!m_Controls.AnyTouch) return;
            if (m_Controls.MoveActive) { m_Move.Direction = m_Controls.Move; into.Add(m_Move); }
            if (m_Controls.HasAim) { m_Aim.Yaw = m_Controls.AimYaw; into.Add(m_Aim); }
            if (m_Controls.Fire) into.Add(m_Fire);
            if (m_Controls.Dash) into.Add(m_Dash);
        }
    }
}
