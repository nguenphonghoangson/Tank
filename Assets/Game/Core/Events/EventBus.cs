using System;
using System.Collections.Generic;

namespace Tank.Core.Events
{
    /// <summary>Typed publish/subscribe. Systems announce what happened; views, HUD and rules react, none of them knowing each other.</summary>
    public interface IEventBus
    {
        IDisposable Subscribe<T>(Action<T> handler);
        void Publish<T>(T evt);
    }

    public sealed class EventBus : IEventBus
    {
        // copy-on-write handler arrays: publishing allocates nothing, and a handler may (un)subscribe while an event is being delivered
        readonly Dictionary<Type, Delegate[]> m_Handlers = new Dictionary<Type, Delegate[]>();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Type key = typeof(T);
            m_Handlers.TryGetValue(key, out Delegate[] current);
            int n = current == null ? 0 : current.Length;
            var next = new Delegate[n + 1];
            if (n > 0) Array.Copy(current, next, n);
            next[n] = handler;
            m_Handlers[key] = next;
            return new Subscription(() => Remove(key, handler));
        }

        public void Publish<T>(T evt)
        {
            if (!m_Handlers.TryGetValue(typeof(T), out Delegate[] handlers)) return;
            for (int i = 0; i < handlers.Length; i++) ((Action<T>)handlers[i])(evt);
        }

        void Remove(Type key, Delegate handler)
        {
            if (!m_Handlers.TryGetValue(key, out Delegate[] current)) return;
            int index = Array.IndexOf(current, handler);
            if (index < 0) return;
            if (current.Length == 1) { m_Handlers.Remove(key); return; }
            var next = new Delegate[current.Length - 1];
            Array.Copy(current, 0, next, 0, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            m_Handlers[key] = next;
        }

        sealed class Subscription : IDisposable
        {
            Action m_Dispose;
            public Subscription(Action dispose) { m_Dispose = dispose; }
            public void Dispose() { m_Dispose?.Invoke(); m_Dispose = null; }
        }
    }
}
