using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>One line of the kill feed: killer, a crosshair, victim. A line that involves you gets a highlight behind it.</summary>
    public sealed class KillFeedEntryView : MonoBehaviour
    {
        [SerializeField] TMP_Text killer, victim;
        [SerializeField] GameObject killerBlock;
        [SerializeField] Image highlight;
        [SerializeField] CanvasGroup group;

        public float Born { get; set; }

        public void Show(string killerName, Color killerColor, string victimName, Color victimColor, bool involvesMe)
        {
            killerBlock.SetActive(killerName != null);
            if (killerName != null) { killer.text = killerName; killer.color = killerColor; }
            victim.text = victimName; victim.color = victimColor;
            highlight.enabled = involvesMe;
        }

        public void SetAlpha(float a) { group.alpha = a; }
    }
}
