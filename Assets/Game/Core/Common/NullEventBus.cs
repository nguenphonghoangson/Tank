using System;

namespace Tank.Core.Events
{
    /// <summary>An event bus that drops everything. Used where a simulation is run only to predict (replays must not announce anything twice).</summary>
    public sealed class NullEventBus : IEventBus
    {
        sealed class Nothing : IDisposable { public void Dispose() { } }
        static readonly IDisposable s_Nothing = new Nothing();
        public IDisposable Subscribe<T>(Action<T> handler) { return s_Nothing; }
        public void Publish<T>(T evt) { }
    }
}
