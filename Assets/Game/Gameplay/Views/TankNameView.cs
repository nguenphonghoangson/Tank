using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>The player's name over the tank, in their colour, kept facing the camera like the health bar.</summary>
    public sealed class TankNameView : MonoBehaviour
    {
        [SerializeField] TextMesh label;
        [SerializeField] float cameraPitch = 59f;
        [SerializeField] float localScale = 1.5f;

        public void Set(string playerName, Color color)
        {
            if (label == null) return;
            label.text = playerName;
            label.color = color;
        }

        public void SetEmphasis(bool on) { transform.localScale = Vector3.one * (on ? localScale : 1f); }

        public void Show(bool visible) { gameObject.SetActive(visible); }

        void LateUpdate() { transform.rotation = Quaternion.Euler(cameraPitch, 0f, 0f); }
    }
}
