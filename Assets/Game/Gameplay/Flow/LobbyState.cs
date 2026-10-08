using System;

namespace Tank.Gameplay.Flow
{
    /// <summary>Whether the match is still waiting in the lobby. The network layer sets it on every machine; the interface only reads it.</summary>
    public sealed class LobbyState
    {
        bool m_Active;
        public bool Active { get => m_Active; set { if (m_Active == value) return; m_Active = value; Changed?.Invoke(value); } }
        public event Action<bool> Changed;
    }
}
