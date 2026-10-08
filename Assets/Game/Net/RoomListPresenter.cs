using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Tank.Net
{
    /// <summary>The list of rooms found on the network. It listens while the main menu is up, drops a room that stops answering, and joins the one pressed.</summary>
    public sealed class RoomListPresenter : MonoBehaviour
    {
        [SerializeField] NetMenuController controller;
        [SerializeField] RoomDiscovery discovery;
        [SerializeField] RectTransform rowsRoot;
        [SerializeField] RoomRowView rowPrefab;
        [SerializeField] TMP_Text emptyText;
        [SerializeField, Min(1f)] float forgetAfterSeconds = 8f;
        [SerializeField, Min(1)] int maxRows = 6;

        sealed class Seen { public RoomInfo Info; public float At; }
        readonly Dictionary<long, Seen> m_Rooms = new Dictionary<long, Seen>();
        readonly List<RoomRowView> m_Rows = new List<RoomRowView>();
        readonly List<long> m_Order = new List<long>();
        bool m_Listening;

        void OnEnable() { discovery.Found += OnFound; }
        void OnDisable() { discovery.Found -= OnFound; }

        void OnFound(RoomInfo room) { m_Rooms[room.serverId] = new Seen { Info = room, At = Time.unscaledTime }; }

        void Update()
        {
            bool menu = !controller.Started;
            if (menu && !m_Listening) { m_Listening = true; try { discovery.StartDiscovery(); } catch (System.Exception e) { Debug.LogWarning("Room search unavailable: " + e.Message); } }
            else if (!menu && m_Listening) { m_Listening = false; discovery.StopDiscovery(); }
            if (!menu) return;

            m_Order.Clear();
            foreach (var pair in m_Rooms) m_Order.Add(pair.Key);
            foreach (long id in m_Order) if (Time.unscaledTime - m_Rooms[id].At > forgetAfterSeconds) { m_Rooms.Remove(id); }
            m_Order.Clear();
            foreach (var pair in m_Rooms) m_Order.Add(pair.Key);
            m_Order.Sort();

            int shown = Mathf.Min(m_Order.Count, maxRows);
            emptyText.gameObject.SetActive(shown == 0);
            for (int i = 0; i < shown; i++)
            {
                RoomRowView row = Row(i);
                RoomInfo info = m_Rooms[m_Order[i]].Info;
                row.Bind(info);
                row.SetTarget(info.uri.Host);
            }
            for (int i = shown; i < m_Rows.Count; i++) m_Rows[i].gameObject.SetActive(false);
        }

        RoomRowView Row(int index)
        {
            while (m_Rows.Count <= index)
            {
                RoomRowView row = Instantiate(rowPrefab, rowsRoot);
                row.Chosen += () => controller.JoinAddress(row.Target);
                m_Rows.Add(row);
            }
            m_Rows[index].gameObject.SetActive(true);
            return m_Rows[index];
        }
    }
}
