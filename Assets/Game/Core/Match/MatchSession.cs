using Tank.Core.Common;
using Tank.Core.Events;

namespace Tank.Core.Match
{
    public enum MatchPhase { Warmup, Playing, Ended }

    public readonly struct MatchSettings
    {
        public readonly float WarmupSeconds, DurationSeconds;
        public MatchSettings(float warmupSeconds, float durationSeconds) { WarmupSeconds = warmupSeconds; DurationSeconds = durationSeconds; }
    }

    public interface IMatchQuery
    {
        MatchPhase Phase { get; }
        float TimeLeft { get; }          // counts the warmup down while warming up, the match while playing
        int WinnerId { get; }            // valid in Ended: a tank id, or -1 for a draw
    }

    /// <summary>
    /// The match flow: Warmup, Playing, Ended. States do the timing; the session holds the shared data and announces phase changes.
    /// What happens around the edges (who respawns when, when to restart) is the director's job, not the rules'.
    /// </summary>
    public sealed class MatchSession : IMatchQuery, ITickable
    {
        readonly MatchSettings m_Settings;
        readonly IRankingQuery m_Ranking;
        readonly IEventBus m_Bus;
        readonly StateMachine<MatchSession> m_Machine;

        public MatchPhase Phase { get; private set; } = MatchPhase.Ended;
        public float TimeLeft { get; internal set; }
        public int WinnerId { get; private set; } = -1;
        public MatchSettings Settings => m_Settings;

        public MatchSession(MatchSettings settings, IRankingQuery ranking, IEventBus bus)
        {
            m_Settings = settings; m_Ranking = ranking; m_Bus = bus;
            m_Machine = new StateMachine<MatchSession>(this);
        }

        public void StartWarmup() { Enter(MatchPhase.Warmup, new WarmupState()); }
        public void Tick(float dt) { m_Machine.Tick(dt); }

        internal void BeginPlaying() { Enter(MatchPhase.Playing, new PlayingState()); }

        internal void End()
        {
            WinnerId = m_Ranking.TryGetWinner(out int id) ? id : -1;
            Enter(MatchPhase.Ended, new EndedState());
            m_Bus.Publish(new MatchEnded(WinnerId));
        }

        void Enter(MatchPhase phase, IState<MatchSession> state)
        {
            Phase = phase;
            m_Machine.Change(state);
            m_Bus.Publish(new MatchPhaseChanged(phase));
        }

        sealed class WarmupState : IState<MatchSession>
        {
            public void Enter(MatchSession s) { s.TimeLeft = s.m_Settings.WarmupSeconds; s.WinnerId = -1; }
            public void Tick(MatchSession s, float dt) { s.TimeLeft -= dt; if (s.TimeLeft <= 0f) s.BeginPlaying(); }
            public void Exit(MatchSession s) { }
        }

        sealed class PlayingState : IState<MatchSession>
        {
            public void Enter(MatchSession s) { s.TimeLeft = s.m_Settings.DurationSeconds; }
            public void Tick(MatchSession s, float dt) { s.TimeLeft -= dt; if (s.TimeLeft <= 0f) { s.TimeLeft = 0f; s.End(); } }
            public void Exit(MatchSession s) { }
        }

        sealed class EndedState : IState<MatchSession>
        {
            public void Enter(MatchSession s) { }
            public void Tick(MatchSession s, float dt) { }
            public void Exit(MatchSession s) { }
        }
    }

    /// <summary>The fixed order a tick runs in. One class owns the order; each system stays unaware of the others.</summary>
    public sealed class SimulationPipeline : ITickable
    {
        readonly ITickable[] m_Steps;
        public SimulationPipeline(params ITickable[] steps) { m_Steps = steps; }
        public void Tick(float dt) { for (int i = 0; i < m_Steps.Length; i++) m_Steps[i].Tick(dt); }
    }

    public sealed class ActionTickable : ITickable
    {
        readonly System.Action<float> m_Action;
        public ActionTickable(System.Action<float> action) { m_Action = action; }
        public void Tick(float dt) { m_Action(dt); }
    }
}
