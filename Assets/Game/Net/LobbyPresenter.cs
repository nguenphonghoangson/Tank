using System.Collections.Generic;
using Reflex.Attributes;
using TMPro;
using Tank.Core.Combat;
using Tank.Gameplay.Config;
using Tank.Gameplay.Flow;
using Tank.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Net
{
    /// <summary>
    /// The room a hosted match waits in: who has joined, how many bots to add, and a START button that only the host sees.
    /// Everyone else sees the same list and a waiting message; the screen closes on its own when the host starts.
    /// </summary>
    public sealed class LobbyPresenter : MonoBehaviour
    {
        [SerializeField] NetMenuController controller;
        [SerializeField] CanvasGroup group;
        [SerializeField] RectTransform listRoot;
        [SerializeField] TMP_Text rowPrefab;
        [SerializeField] TMP_Text countText, waitingText;
        [SerializeField] GameObject hostControls;
        [SerializeField] TMP_InputField botsField;
        [SerializeField] Button startButton;

        readonly List<TMP_Text> m_Rows = new List<TMP_Text>();
        bool m_Shown;
        LobbyState m_Lobby; ITankRegistry m_Tanks; PlayerPalette m_Palette;

        [Inject]
        void Construct(LobbyState lobby, ITankRegistry tanks, PlayerPalette palette)
        {
            m_Lobby = lobby; m_Tanks = tanks; m_Palette = palette;
            startButton.onClick.AddListener(() => controller.StartMatch(int.TryParse(botsField.text, out int n) ? Mathf.Clamp(n, 0, 7) : 0));
        }

        void LateUpdate()
        {
            if (m_Lobby == null) return;
            bool show = m_Lobby.Active;
            group.alpha = show ? 1f : 0f; group.blocksRaycasts = show; group.interactable = show;
            if (!show) { m_Shown = false; return; }
            if (!m_Shown) { m_Shown = true; botsField.text = controller.BotCount; }       // starts from what was typed in the menu

            bool host = controller.IsServer;
            hostControls.SetActive(host); waitingText.gameObject.SetActive(!host);

            int humans = 0;
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel t = m_Tanks.All[i];
                if (t.IsBot) continue;
                TMP_Text row = Row(humans++);
                row.text = "<color=#" + ColorUtility.ToHtmlStringRGB(m_Palette.Get(t.ColorSlot).color) + ">" + t.Name + "</color>";
            }
            for (int i = humans; i < m_Rows.Count; i++) m_Rows[i].gameObject.SetActive(false);
            countText.text = humans + (humans == 1 ? " player" : " players");
        }

        TMP_Text Row(int index)
        {
            while (m_Rows.Count <= index) { TMP_Text r = Instantiate(rowPrefab, listRoot); m_Rows.Add(r); }
            m_Rows[index].gameObject.SetActive(true);
            return m_Rows[index];
        }
    }
}
