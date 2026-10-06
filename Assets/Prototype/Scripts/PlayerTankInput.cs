using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.Prototype
{
    /// <summary>
    /// PC/Mac input source: WASD/arrows move, mouse aims the turret, left mouse fires, R reloads, Space/Shift uses the skill.
    /// It only writes a TankCommand. A mobile source (two sticks) would write the same struct.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerTankInput : MonoBehaviour
    {
        public TankUnit tank;
        public Camera cam;
        public CameraRig rig;
        public Transform reticle;
        public float aimHeight = 0.8f;

        void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null || cam == null) return;

            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);

            TankCommand cmd = tank.Command;
            cmd.Move = new Vector2(x, z);
            cmd.Fire = mouse.leftButton.isPressed;
            cmd.Reload = kb.rKey.isPressed;
            cmd.Skill = kb.spaceKey.isPressed || kb.leftShiftKey.isPressed;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            Plane plane = new Plane(Vector3.up, new Vector3(0f, aimHeight, 0f));
            if (plane.Raycast(ray, out float d)) cmd.AimPoint = ray.GetPoint(d);

            tank.Command = cmd;
            if (rig != null) rig.SetAimPoint(cmd.AimPoint);
            if (reticle != null) reticle.position = new Vector3(cmd.AimPoint.x, 0.06f, cmd.AimPoint.z);
        }
    }
}
