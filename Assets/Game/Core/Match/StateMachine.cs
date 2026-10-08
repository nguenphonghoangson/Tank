namespace Tank.Core.Match
{
    public interface IState<in TOwner>
    {
        void Enter(TOwner owner);
        void Tick(TOwner owner, float dt);
        void Exit(TOwner owner);
    }

    /// <summary>A small state machine: one active state, which decides when to hand over to the next.</summary>
    public sealed class StateMachine<TOwner>
    {
        readonly TOwner m_Owner;
        IState<TOwner> m_Current;

        public StateMachine(TOwner owner) { m_Owner = owner; }
        public IState<TOwner> Current => m_Current;

        public void Change(IState<TOwner> next)
        {
            m_Current?.Exit(m_Owner);
            m_Current = next;
            m_Current?.Enter(m_Owner);
        }

        public void Tick(float dt) { m_Current?.Tick(m_Owner, dt); }
    }
}
