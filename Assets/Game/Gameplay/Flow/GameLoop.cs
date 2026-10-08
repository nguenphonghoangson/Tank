using Reflex.Attributes;
using Tank.Core.Common;
using Tank.Core.Events;
using Tank.Gameplay.Config;
using UnityEngine;

namespace Tank.Gameplay.Flow
{
    /// <summary>Runs the simulation at a fixed rate whatever the frame rate is, and tells views how far into the next tick the frame is.</summary>
    public sealed class GameLoop : MonoBehaviour, IRenderClock
    {
        ISimulationDriver m_Driver;
        IEventBus m_Bus;
        float m_Step = 1f / 30f, m_Accumulator;

        public float Alpha { get; private set; }
        public float Step => m_Step;

        [Inject]
        void Construct(ISimulationDriver driver, MatchConfig config, IEventBus bus)
        {
            m_Driver = driver; m_Bus = bus; m_Step = config.FixedStep;
        }

        void Update()
        {
            if (m_Driver == null) return;
            m_Accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);     // a long hitch does not make the game catch up for seconds
            int guard = 0;
            while (m_Accumulator >= m_Step && guard++ < 5)
            {
                m_Accumulator -= m_Step;
                m_Driver.Tick(m_Step);
                m_Bus.Publish(new SimulationTicked());
            }
            Alpha = Mathf.Clamp01(m_Accumulator / m_Step);
        }
    }
}
