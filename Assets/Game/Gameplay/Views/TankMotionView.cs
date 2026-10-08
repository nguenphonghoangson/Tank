using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>Puts the tank where the simulation says it is, hull and turret.</summary>
    public sealed class TankMotionView : MonoBehaviour
    {
        [SerializeField] TankVisual visual;

        public void Present(Vector2 position, float hullYaw, float turretYaw)
        {
            transform.SetPositionAndRotation(new Vector3(position.x, 0f, position.y), Quaternion.Euler(0f, hullYaw, 0f));
            if (visual != null) visual.SetTurretYaw(turretYaw);
        }
    }
}
