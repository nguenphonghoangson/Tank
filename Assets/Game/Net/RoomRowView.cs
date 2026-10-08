using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Net
{
    /// <summary>One room in the list: the host's name, how many are in, and whether it is still waiting to start. Pressing it asks to join.</summary>
    public sealed class RoomRowView : MonoBehaviour
    {
        [SerializeField] TMP_Text title, status;
        [SerializeField] Button button;

        public event Action Chosen;
        public string Target { get; private set; }

        public void SetTarget(string address) { Target = address; }

        void Awake() { button.onClick.AddListener(() => Chosen?.Invoke()); }

        public void Bind(RoomInfo room)
        {
            title.text = string.IsNullOrEmpty(room.hostName) ? "Room" : room.hostName + "'s room";
            status.text = room.players + "/" + room.maxPlayers + "   " + (room.inLobby ? "LOBBY" : "IN MATCH");
        }
    }
}
