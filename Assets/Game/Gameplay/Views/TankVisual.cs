using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>The tank's model: hull and turret. Knows how to be shown, hidden and have its turret turned, nothing else.</summary>
    public sealed class TankVisual : MonoBehaviour
    {
        [SerializeField] Transform turret;
        [SerializeField] GameObject body;

        public Transform Turret => turret;

        public void Show(bool visible) { if (body != null) body.SetActive(visible); }
        public void SetTurretYaw(float degrees) { if (turret != null) turret.rotation = Quaternion.Euler(0f, degrees, 0f); }
    }
}
