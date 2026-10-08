using Tank.Core.Input;
using Tank.Gameplay.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tank.Gameplay.Input
{
    /// <summary>
    /// Twin-stick touch controls: the first finger down on the left half is the move stick, on the right half the aim stick (which also
    /// fires while pushed), and a finger on the round button near the right edge dashes. The sticks float: they start where the finger
    /// lands. This class reads the fingers; drawing them is TouchControlsPresenter's job.
    /// </summary>
    public sealed class TouchControls
    {
        const float DeadZone = 0.12f, FireThreshold = 0.35f;

        readonly IPointerBlocker m_Blocker;
        int m_MoveId = -1, m_AimId = -1, m_DashId = -1;

        public TouchControls(IPointerBlocker blocker) { m_Blocker = blocker; }

        public bool Present { get; private set; }               // the device has a touchscreen at all
        public bool MoveActive => m_MoveId >= 0;
        public bool AimActive => m_AimId >= 0;
        public Vector2 MoveOrigin { get; private set; }
        public Vector2 MoveFinger { get; private set; }
        public Vector2 AimOrigin { get; private set; }
        public Vector2 AimFinger { get; private set; }
        public Vector2 Move { get; private set; }
        public bool HasAim { get; private set; }
        public float AimYaw { get; private set; }
        public bool Fire { get; private set; }
        public bool Dash => m_DashId >= 0;
        public bool AnyTouch => m_MoveId >= 0 || m_AimId >= 0 || m_DashId >= 0;

        /// <summary>The stick travel for full strength, and the dash button, scale with the screen so they feel the same on any phone.</summary>
        public static float StickRadius => Screen.height * 0.13f;
        public static Vector2 DashCenter => new Vector2(Screen.width - Screen.height * 0.16f, Screen.height * 0.42f);
        public static float DashRadius => Screen.height * 0.09f;

        public void Update()
        {
            Touchscreen screen = Touchscreen.current;
            Present = screen != null;
            if (screen == null) { Release(); return; }

            bool moveSeen = false, aimSeen = false, dashSeen = false;
            foreach (var touch in screen.touches)
            {
                if (!touch.isInProgress) continue;
                int id = touch.touchId.ReadValue();
                Vector2 pos = touch.position.ReadValue();
                if (id == m_MoveId) { moveSeen = true; MoveFinger = pos; }
                else if (id == m_AimId) { aimSeen = true; AimFinger = pos; }
                else if (id == m_DashId) dashSeen = true;
                else if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began && !m_Blocker.IsOverInteractive(pos))     // a finger that lands on a button belongs to the button
                {
                    if ((pos - DashCenter).sqrMagnitude <= DashRadius * DashRadius * 1.5f && m_DashId < 0) { m_DashId = id; dashSeen = true; }
                    else if (pos.x < Screen.width * 0.5f && m_MoveId < 0) { m_MoveId = id; MoveOrigin = MoveFinger = pos; moveSeen = true; }
                    else if (pos.x >= Screen.width * 0.5f && m_AimId < 0) { m_AimId = id; AimOrigin = AimFinger = pos; aimSeen = true; }
                }
            }
            if (!moveSeen) m_MoveId = -1;
            if (!aimSeen) m_AimId = -1;
            if (!dashSeen) m_DashId = -1;

            Move = m_MoveId >= 0 ? StickMath.Evaluate(MoveOrigin, MoveFinger, StickRadius, DeadZone) : Vector2.zero;
            Vector2 aim = m_AimId >= 0 ? StickMath.Evaluate(AimOrigin, AimFinger, StickRadius, DeadZone) : Vector2.zero;
            HasAim = aim.sqrMagnitude > 0f;
            if (HasAim) AimYaw = StickMath.YawDegrees(aim);
            Fire = aim.magnitude >= FireThreshold;
        }

        void Release()
        {
            m_MoveId = m_AimId = m_DashId = -1;
            Move = Vector2.zero; HasAim = false; Fire = false;
        }
    }
}
