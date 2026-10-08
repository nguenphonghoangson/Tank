using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tank.Gameplay.Input
{
    /// <summary>WASD or arrows to drive, the mouse to aim at a point on the ground, left button to fire, space to dash.</summary>
    public sealed class KeyboardMouseCommandSource : ICommandSource
    {
        readonly MoveCommand m_Move = new MoveCommand();
        readonly AimCommand m_Aim = new AimCommand();
        readonly FireCommand m_Fire = new FireCommand();
        readonly DashCommand m_Dash = new DashCommand();
        readonly Plane m_Ground = new Plane(Vector3.up, Vector3.zero);

        public void Collect(TankModel tank, List<ITankCommand> into)
        {
            Keyboard kb = Keyboard.current; Mouse mouse = Mouse.current;
            if (kb != null)
            {
                Vector2 move = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                m_Move.Direction = move;
                into.Add(m_Move);
                if (kb.spaceKey.isPressed) into.Add(m_Dash);       // held: the dash cooldown stops repeats, and a tap is not lost between ticks
            }

            Camera cam = Camera.main;
            if (mouse != null && cam != null)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                if (m_Ground.Raycast(ray, out float t))
                {
                    Vector3 hit = ray.GetPoint(t);
                    Vector2 to = new Vector2(hit.x, hit.z) - tank.Position;
                    if (to.sqrMagnitude > 0.25f) { m_Aim.Yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg; into.Add(m_Aim); }
                }
                if (mouse.leftButton.isPressed) into.Add(m_Fire);
            }
        }
    }
}
