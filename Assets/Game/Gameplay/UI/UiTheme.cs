using TMPro;
using UnityEngine;

namespace Tank.Gameplay.UI
{
    /// <summary>The look of the interface in one place: sprites, fonts and colours. Presenters take their colours from here, so a re-skin is editing this asset.</summary>
    [CreateAssetMenu(menuName = "Tank/UI/Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Panels")]
        public Sprite panelSmall;
        public Sprite panelLarge;
        public Sprite rounded;
        public Sprite square;
        public Sprite disc;
        public Sprite ring;
        public Sprite vignette;

        [Header("Icons")]
        public Sprite iconHealth, iconAmmo, iconSpeed, iconDamage, iconDash, iconShield, iconKill, crown;

        [Header("Fonts")]
        public TMP_FontAsset body;
        public TMP_FontAsset heading;

        [Header("Colours")]
        public Color text = Color.white;
        public Color textMuted = new Color(1f, 1f, 1f, 0.62f);
        public Color panelTint = new Color(1f, 1f, 1f, 0.94f);
        public Color accent = new Color(1f, 0.82f, 0.2f);
        public Color good = new Color(0.4f, 1f, 0.5f);
        public Color bad = new Color(1f, 0.3f, 0.25f);
        public Color shield = new Color(0.35f, 0.85f, 1f);
        public Color highlight = new Color(1f, 0.95f, 0.5f);

        public Color HealthColor(float t) { return Color.Lerp(bad, good, Mathf.Clamp01(t * 1.2f)); }
    }
}
