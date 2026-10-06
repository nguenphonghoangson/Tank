using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace TankGame.Prototype
{
    /// <summary>
    /// Touch input source: a floating move stick on the left half, a floating aim stick on the right half
    /// (pushing it past the threshold fires), plus Dash and Reload buttons. Like the keyboard source it only writes a
    /// TankCommand. The HUD draws the sticks from the public state below.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MobileTankInput : MonoBehaviour
    {
        public TankUnit tank;
        public float aimDistance = 14f;
        public float fireThreshold = 0.3f;
        public float aimDeadzone = 0.12f;

        // state for drawing (screen pixels, origin bottom-left)
        public bool MoveActive { get; private set; }
        public bool AimActive { get; private set; }
        public Vector2 MoveOrigin { get; private set; }
        public Vector2 MoveNow { get; private set; }
        public Vector2 AimOrigin { get; private set; }
        public Vector2 AimNow { get; private set; }
        public bool Firing { get; private set; }
        public bool DashHeld { get; private set; }
        public bool ReloadHeld { get; private set; }
        public int ActiveTouchCount { get; private set; }

        /// <summary>Stick radius in pixels: about 0.45 inch on a phone.</summary>
        public float StickRadius => Mathf.Max(70f, (Screen.dpi > 0f ? Screen.dpi : 160f) * 0.45f);

        public Rect DashRect
        {
            get { float r = StickRadius; return new Rect(Screen.width - r * 1.0f - r * 1.6f, r * 2.3f, r * 1.0f, r * 1.0f); }
        }

        public Rect ReloadRect
        {
            get { float r = StickRadius; return new Rect(Screen.width - r * 1.0f - r * 0.2f, r * 3.6f, r * 0.9f, r * 0.9f); }
        }

        int m_MoveId = -1, m_AimId = -1;
        Vector2 m_AimDir = Vector2.up;
        bool m_HasAim;

        void OnEnable() { EnhancedTouchSupport.Enable(); }
        void OnDisable() { EnhancedTouchSupport.Disable(); ResetState(); }

        void ResetState()
        {
            m_MoveId = m_AimId = -1;
            MoveActive = AimActive = Firing = DashHeld = ReloadHeld = false;
        }

        void Update()
        {
            if (tank == null) return;
            if (Time.timeScale == 0f) { tank.Command = default; return; }   // core selection pauses the match

            float R = StickRadius;
            bool moveSeen = false, aimSeen = false;
            DashHeld = ReloadHeld = false;
            Rect dash = DashRect, reload = ReloadRect;

            ActiveTouchCount = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count;
            foreach (UnityEngine.InputSystem.EnhancedTouch.Touch t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                Vector2 p = t.screenPosition;
                int id = t.touchId;

                if (dash.Contains(p)) { DashHeld = true; continue; }
                if (reload.Contains(p)) { ReloadHeld = true; continue; }

                if (id == m_MoveId) { MoveNow = p; moveSeen = true; continue; }
                if (id == m_AimId) { AimNow = p; aimSeen = true; continue; }
                // a new finger takes whichever stick is free on its side of the screen
                if (p.x < Screen.width * 0.5f && m_MoveId < 0) { m_MoveId = id; MoveOrigin = MoveNow = p; moveSeen = true; }
                else if (p.x >= Screen.width * 0.5f && m_AimId < 0) { m_AimId = id; AimOrigin = AimNow = p; aimSeen = true; }
            }
            if (!moveSeen) m_MoveId = -1;
            if (!aimSeen) m_AimId = -1;
            MoveActive = m_MoveId >= 0;
            AimActive = m_AimId >= 0;

            TankCommand cmd = tank.Command;
            cmd.Move = MoveActive ? Vector2.ClampMagnitude((MoveNow - MoveOrigin) / R, 1f) : Vector2.zero;
            if (cmd.Move.magnitude < 0.12f) cmd.Move = Vector2.zero;

            Firing = false;
            if (AimActive)
            {
                Vector2 a = Vector2.ClampMagnitude((AimNow - AimOrigin) / R, 1f);
                if (a.magnitude > aimDeadzone) { m_AimDir = a.normalized; m_HasAim = true; }
                Firing = a.magnitude > fireThreshold;
            }
            Vector3 dir = m_HasAim ? new Vector3(m_AimDir.x, 0f, m_AimDir.y) : tank.transform.forward;
            cmd.AimPoint = tank.transform.position + dir * aimDistance + Vector3.up * 0.8f;
            cmd.Fire = Firing;
            cmd.Skill = DashHeld;
            cmd.Reload = ReloadHeld;
            tank.Command = cmd;
        }
    }
}
