using Reflex.Attributes;
using TMPro;
using Tank.Core.Combat;
using Tank.Gameplay.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>
    /// Draws the touch controls on the screen: the left half is the move stick, the right half the aim-and-fire stick, plus a dash button.
    /// Both zones show a faint hint until a thumb is down, then the stick follows the thumb. TouchControls reads the fingers; this only shows them.
    /// </summary>
    public sealed class TouchControlsPresenter : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] RectTransform moveHint, moveBase, moveKnob;
        [SerializeField] RectTransform aimHint, aimBase, aimKnob;
        [SerializeField] Image aimKnobImage;
        [SerializeField] RectTransform dashButton;
        [SerializeField] Image dashFill, dashBack;
        [SerializeField] TMP_Text dashLabel;
        [SerializeField] RectTransform splitLine;
        [SerializeField] Canvas canvas;

        TouchControls m_Controls; ITankRegistry m_Tanks; ILocalPlayer m_Local;
        RectTransform m_CanvasRect;

        [Inject]
        void Construct(TouchControls controls, ITankRegistry tanks, ILocalPlayer local)
        {
            m_Controls = controls; m_Tanks = tanks; m_Local = local;
            m_CanvasRect = (RectTransform)group.transform;      // positions are measured from its centre, so the elements anchor to its centre
        }

        void Update() { if (m_Controls != null) m_Controls.Update(); }

        void LateUpdate()
        {
            if (m_Controls == null) return;
            bool on = m_Controls.Present;
            group.alpha = on ? 1f : 0f;
            if (!on) return;

            float unit = 1f / canvas.scaleFactor;                          // screen pixels to canvas units
            float stickSize = TouchControls.StickRadius * 2f * unit;
            Place(moveHint, new Vector2(Screen.height * 0.22f, Screen.height * 0.27f), stickSize, !m_Controls.MoveActive);
            Place(aimHint, new Vector2(Screen.width - Screen.height * 0.22f, Screen.height * 0.27f), stickSize, !m_Controls.AimActive);

            if (m_Controls.MoveActive) { Place(moveBase, m_Controls.MoveOrigin, stickSize, true); Place(moveKnob, Knob(m_Controls.MoveOrigin, m_Controls.MoveFinger), stickSize * 0.45f, true); }
            else { moveBase.gameObject.SetActive(false); moveKnob.gameObject.SetActive(false); }
            if (m_Controls.AimActive) { Place(aimBase, m_Controls.AimOrigin, stickSize, true); Place(aimKnob, Knob(m_Controls.AimOrigin, m_Controls.AimFinger), stickSize * 0.45f, true); aimKnobImage.color = m_Controls.Fire ? new Color(1f, 0.45f, 0.35f, 0.9f) : new Color(1f, 1f, 1f, 0.55f); }
            else { aimBase.gameObject.SetActive(false); aimKnob.gameObject.SetActive(false); }

            TankModel me = m_Tanks.Find(m_Local.TankId);
            float cooldown = me != null ? me.Dash.Cooldown : 0f;
            Place(dashButton, TouchControls.DashCenter, TouchControls.DashRadius * 2f * unit, true);
            dashBack.color = m_Controls.Dash ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, cooldown > 0.05f ? 0.15f : 0.32f);
            dashFill.fillAmount = cooldown > 0.05f ? Mathf.Clamp01(cooldown / 4f) : 0f;
            dashLabel.text = cooldown > 0.05f ? cooldown.ToString("0.0") : "DASH";
        }

        static Vector2 Knob(Vector2 origin, Vector2 finger) { return origin + Vector2.ClampMagnitude(finger - origin, TouchControls.StickRadius); }

        void Place(RectTransform rt, Vector2 screenPoint, float size, bool visible)
        {
            rt.gameObject.SetActive(visible);
            if (!visible) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_CanvasRect, screenPoint, null, out Vector2 local);
            rt.anchoredPosition = local; rt.sizeDelta = Vector2.one * size;
        }
    }
}
