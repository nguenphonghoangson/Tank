using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>How battered the tank looks: the hull and turret models swap for dented and then wrecked ones as health drops.</summary>
    public sealed class TankDamageView : MonoBehaviour
    {
        public const int StageCount = 3;     // 0 intact, 1 damaged, 2 critical

        [SerializeField] MeshFilter hull;
        [SerializeField] MeshFilter turret;
        [SerializeField] Mesh[] hullStages = new Mesh[StageCount];
        [SerializeField] Mesh[] turretStages = new Mesh[StageCount];
        [SerializeField, Range(0f, 1f)] float damagedBelow = 0.66f;
        [SerializeField, Range(0f, 1f)] float criticalBelow = 0.33f;

        public int Stage { get; private set; }

        public int SetHealth01(float health)
        {
            int stage = health <= criticalBelow ? 2 : health <= damagedBelow ? 1 : 0;
            if (stage != Stage || hull.sharedMesh != hullStages[stage]) Apply(stage);
            return Stage;
        }

        void Apply(int stage)
        {
            Stage = stage;
            if (hull != null && hullStages[stage] != null) hull.sharedMesh = hullStages[stage];
            if (turret != null && turretStages[stage] != null) turret.sharedMesh = turretStages[stage];
        }
    }
}
