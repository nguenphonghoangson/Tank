using UnityEngine;

namespace Tank.Gameplay.Config
{
    [CreateAssetMenu(menuName = "Tank/Config/Match", fileName = "MatchConfig")]
    public sealed class MatchConfig : ScriptableObject
    {
        public float warmupSeconds = 3f;
        public float durationSeconds = 300f;
        public float restartDelaySeconds = 10f;
        [Min(0)] public int botCount = 3;
        [Tooltip("Simulation steps per second, independent of the frame rate.")]
        public int tickRate = 30;
        public int randomSeed = 12345;
        [Header("Cores")]
        [Tooltip("A new core is offered at the start of each of this many equal parts of the match.")] public int corePhases = 4;
        public float coreOfferSeconds = 12f;
        [Header("Items")]
        public float itemPickupRadius = 2.2f;
        public float FixedStep => 1f / Mathf.Max(1, tickRate);
    }
}
