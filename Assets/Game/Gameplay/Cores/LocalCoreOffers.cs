using System;
using System.Collections.Generic;
using Tank.Core.Events;
using Tank.Core.Match;
using UnityEngine;

namespace Tank.Gameplay.Cores
{
    /// <summary>The core choice currently on this player's screen, if any. It follows the offer and pick events, so it is the same offline, on a host and on a client.</summary>
    public sealed class LocalCoreOffers : IDisposable
    {
        readonly ILocalPlayer m_Local;
        readonly List<IDisposable> m_Subscriptions = new List<IDisposable>();
        float m_Seconds, m_OfferedAt;

        public bool Active { get; private set; }
        public int[] CoreIds { get; private set; }
        public float SecondsLeft => Active ? Mathf.Max(0f, m_Seconds - (Time.unscaledTime - m_OfferedAt)) : 0f;

        public LocalCoreOffers(IEventBus bus, ILocalPlayer local)
        {
            m_Local = local;
            m_Subscriptions.Add(bus.Subscribe<CoreOffered>(e => { if (e.TankId == m_Local.TankId) { Active = true; CoreIds = e.CoreIds; m_Seconds = e.Seconds; m_OfferedAt = Time.unscaledTime; } }));
            m_Subscriptions.Add(bus.Subscribe<CorePicked>(e => { if (e.TankId == m_Local.TankId) Active = false; }));
            m_Subscriptions.Add(bus.Subscribe<MatchPhaseChanged>(e => Active = false));
        }

        public void Dispose() { foreach (IDisposable s in m_Subscriptions) s.Dispose(); m_Subscriptions.Clear(); }
    }
}
