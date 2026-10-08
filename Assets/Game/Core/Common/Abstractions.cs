using System;

namespace Tank.Core.Common
{
    /// <summary>Simulation time in seconds. The game feeds it from the fixed tick, tests from a manual clock.</summary>
    public interface IClock { float Now { get; } }

    /// <summary>Source of randomness, so shots and spawns are repeatable in tests.</summary>
    public interface IRandom
    {
        float Value();                              // [0, 1)
        float Range(float min, float max);
    }

    public sealed class SystemRandom : IRandom
    {
        readonly Random m_Random;
        public SystemRandom(int seed) { m_Random = new Random(seed); }
        public float Value() { return (float)m_Random.NextDouble(); }
        public float Range(float min, float max) { return min + (max - min) * Value(); }
    }

    /// <summary>Anything advanced once per simulation tick.</summary>
    public interface ITickable { void Tick(float dt); }

    public sealed class ManualClock : IClock
    {
        public float Now { get; set; }
        public void Advance(float dt) { Now += dt; }
    }
}
