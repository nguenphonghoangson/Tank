using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>
    /// Where a tank is solid. The game uses no Unity colliders, so this component is the way a hitbox is seen and edited: its two circles
    /// are drawn in the Scene view, and the simulation reads its numbers from here. Change them on the prefab and the rules follow.
    /// </summary>
    public sealed class TankHitbox : MonoBehaviour
    {
        [Tooltip("The circle the tank drives with: it is pushed out of walls and cover by this much.")]
        [Min(0.1f)] public float collisionRadius = 1f;
        [Tooltip("The circle shots hit.")]
        [Min(0.1f)] public float hitRadius = 0.95f;
        [Tooltip("Where shots leave the barrel. Its distance from the tank centre is the muzzle offset the weapons use.")]
        public Transform muzzle;
        [SerializeField, Min(0f)] float muzzleForwardFallback = 1.3f;

        public float MuzzleForward
        {
            get
            {
                if (muzzle == null) return muzzleForwardFallback;
                Vector3 d = muzzle.position - transform.position; d.y = 0f;
                return d.magnitude;
            }
        }

        void OnDrawGizmos()
        {
            GizmoShapes.Circle(transform.position, collisionRadius, new Color(0.2f, 0.9f, 1f, 0.9f));
            GizmoShapes.Circle(transform.position, hitRadius, new Color(1f, 0.35f, 0.3f, 0.9f));
            if (muzzle != null) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(muzzle.position, 0.12f); }
        }
    }
}
