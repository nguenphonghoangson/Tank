using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>One line of a ranking table: place, player colour, name and K / D / A / KDA. Shows what it is given; used by the live panel and the results screen.</summary>
    public sealed class RankingRowView : MonoBehaviour
    {
        [SerializeField] TMP_Text place, playerName, kills, deaths, assists, kda;
        [SerializeField] Image colorPip, background;
        [SerializeField] Color mineBackground = new Color(1f, 0.9f, 0.3f, 0.28f);
        [SerializeField] Color otherBackground = new Color(0f, 0f, 0f, 0f);

        public void Bind(int rank, string name, Color color, int k, int d, int a, float kdaValue, bool mine, Color nameColor)
        {
            place.text = rank.ToString();
            playerName.text = name; playerName.color = mine ? nameColor : Color.white;
            kills.text = k.ToString(); deaths.text = d.ToString(); assists.text = a.ToString();
            kda.text = kdaValue.ToString("0.00");
            colorPip.color = color;
            background.color = mine ? mineBackground : otherBackground;
        }
    }
}
