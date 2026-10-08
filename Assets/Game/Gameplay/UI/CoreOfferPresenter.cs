using Reflex.Attributes;
using TMPro;
using Tank.Core.Cores;
using Tank.Gameplay.Config;
using Tank.Gameplay.Cores;
using Tank.Gameplay.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>
    /// The core pick screen: three cards and a timer. It shows the offer and reports a choice to the ICorePicker; whether that choice
    /// goes straight to the rules or over the network is not its business. On a phone it moves up out of the thumbs' way.
    /// </summary>
    public sealed class CoreOfferPresenter : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] RectTransform panel;
        [SerializeField] CoreCardView[] cards = new CoreCardView[3];
        [SerializeField] RectTransform timerFill;
        [SerializeField] TMP_Text hint;
        [SerializeField] float phoneLift = 150f;

        LocalCoreOffers m_Offers; ICorePicker m_Picker; ILocalPlayer m_Local; CoreConfig m_Cores; TouchControls m_Touch;
        float m_BaseY; bool m_BaseSet;

        [Inject]
        void Construct(LocalCoreOffers offers, ICorePicker picker, ILocalPlayer local, CoreConfig cores, TouchControls touch)
        {
            m_Offers = offers; m_Picker = picker; m_Local = local; m_Cores = cores; m_Touch = touch;
            for (int i = 0; i < cards.Length; i++) { int index = i; cards[i].Chosen += () => Pick(index); }
        }

        void Pick(int index) { if (m_Offers.Active) m_Picker.Pick(m_Local.TankId, index); }

        void LateUpdate()
        {
            if (m_Offers == null) return;
            bool show = m_Offers.Active && m_Offers.CoreIds != null;
            group.alpha = show ? 1f : 0f; group.interactable = show; group.blocksRaycasts = show;
            if (!show) return;

            if (!m_BaseSet) { m_BaseY = panel.anchoredPosition.y; m_BaseSet = true; }
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, m_BaseY + (m_Touch.Present ? phoneLift : 0f));
            hint.text = m_Touch.Present ? "Tap a core" : "Press 1, 2 or 3, or click";

            int[] ids = m_Offers.CoreIds;
            for (int i = 0; i < cards.Length; i++)
            {
                bool has = i < ids.Length;
                cards[i].gameObject.SetActive(has);
                if (!has) continue;
                CoreConfig.Entry c = m_Cores.Presentation(ids[i]);
                cards[i].Show(i, c.name, c.description, c.color);
            }
            timerFill.anchorMax = new Vector2(Mathf.Clamp01(m_Offers.SecondsLeft / 12f), timerFill.anchorMax.y);

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) Pick(0); else if (kb.digit2Key.wasPressedThisFrame) Pick(1); else if (kb.digit3Key.wasPressedThisFrame) Pick(2);
        }
    }
}
