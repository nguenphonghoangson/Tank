using Mirror;
using TMPro;
using Tank.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Net
{
    /// <summary>
    /// The main menu (name, bots, address and the three ways to start) and, once a match is running, the small session bar with the
    /// connection type, ping and a Leave button. It only forwards what the player typed to NetMenuController.
    /// </summary>
    public sealed class MainMenuPresenter : MonoBehaviour
    {
        [SerializeField] NetMenuController controller;
        [SerializeField] GameObject menuRoot, sessionRoot;
        [SerializeField] TMP_InputField nameField, botsField, addressField;
        [SerializeField] Button offlineButton, hostButton, joinButton, hostOnlyButton, leaveButton;
        [SerializeField] TMP_Text sessionText;

        bool m_Filled;

        void Start()
        {
            offlineButton.onClick.AddListener(() => { Apply(); controller.Offline(); });
            hostButton.onClick.AddListener(() => { Apply(); controller.Host(); });
            hostOnlyButton.onClick.AddListener(() => { Apply(); controller.Server(); });
            joinButton.onClick.AddListener(() => { Apply(); controller.Join(); });
            leaveButton.onClick.AddListener(controller.Leave);
        }

        void Apply() { controller.Configure(nameField.text, addressField.text, botsField.text); }

        void LateUpdate()
        {
            if (!m_Filled && !string.IsNullOrEmpty(controller.PlayerName)) { m_Filled = true; nameField.text = controller.PlayerName; addressField.text = controller.Address; botsField.text = controller.BotCount; }
            bool started = controller.Started;
            if (menuRoot.activeSelf == started) menuRoot.SetActive(!started);
            if (sessionRoot.activeSelf != started) sessionRoot.SetActive(started);
            if (!started || controller.Role == null) return;
            SessionRole role = controller.Role.Current;
            sessionText.text = role == SessionRole.Offline ? string.Empty : role + (role == SessionRole.Client ? "  " + Mathf.RoundToInt((float)(NetworkTime.rtt * 1000.0)) + " ms" : string.Empty);
        }
    }
}
