using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>One core on offer: number, name, what it does, in its own colour. Tapping or clicking it raises Chosen.</summary>
    public sealed class CoreCardView : MonoBehaviour
    {
        [SerializeField] TMP_Text number, title, description;
        [SerializeField] Image accent;
        [SerializeField] Button button;

        public event Action Chosen;

        void Awake() { button.onClick.AddListener(() => Chosen?.Invoke()); }

        public void Show(int index, string coreName, string text, Color color)
        {
            number.text = (index + 1).ToString();
            title.text = coreName; title.color = color;
            description.text = text;
            accent.color = color;
        }
    }
}
