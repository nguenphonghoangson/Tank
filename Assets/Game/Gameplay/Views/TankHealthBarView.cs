using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>The bar over a tank: a back plate and a fill, kept facing the camera whatever way the hull points.</summary>
    public sealed class TankHealthBarView : MonoBehaviour
    {
        [SerializeField] Transform fill;
        [SerializeField] float fillWidth = 1.6f;
        [SerializeField] float cameraPitch = 59f;

        public void Show(bool visible) { gameObject.SetActive(visible); }

        public void Set01(float value)
        {
            if (fill == null) return;
            value = Mathf.Clamp01(value);
            fill.localScale = new Vector3(fillWidth * value, fill.localScale.y, 1f);
            fill.localPosition = new Vector3(-fillWidth * (1f - value) * 0.5f, fill.localPosition.y, fill.localPosition.z);
        }

        void LateUpdate() { transform.rotation = Quaternion.Euler(cameraPitch, 0f, 0f); }
    }
}
