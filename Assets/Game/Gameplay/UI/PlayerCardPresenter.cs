using System.Collections.Generic;
using Reflex.Attributes;
using TMPro;
using Tank.Core.Combat;
using Tank.Core.Cores;
using Tank.Gameplay.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>
    /// The card at the bottom of the screen about your own tank: name and colour, health and shield, weapon and ammo, the dash cooldown,
    /// the item buffs still running and the cores you own. It only reads; nothing here changes the tank.
    /// </summary>
    public sealed class PlayerCardPresenter : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] TMP_Text nameText;
        [SerializeField] Image colorPip;
        [Header("Health")]
        [SerializeField] RectTransform healthFill;
        [SerializeField] Image healthFillImage;
        [SerializeField] RectTransform shieldFill;
        [SerializeField] TMP_Text healthText;
        [Header("Weapon and dash")]
        [SerializeField] TMP_Text weaponText;
        [SerializeField] TMP_Text ammoText;
        [SerializeField] RectTransform dashFill;
        [SerializeField] TMP_Text dashText;
        [Header("Buffs")]
        [SerializeField] GameObject shieldChip, speedChip, damageChip;
        [SerializeField] TMP_Text shieldTime, speedTime, damageTime;
        [Header("Cores")]
        [SerializeField] RectTransform coreRoot;
        [SerializeField] TMP_Text coreEntryPrefab;
        [SerializeField] CanvasGroup group;

        readonly List<TMP_Text> m_CoreEntries = new List<TMP_Text>();
        ITankRegistry m_Tanks; ILocalPlayer m_Local; PlayerPalette m_Palette; WeaponConfig m_Weapons; CoreConfig m_Cores; UiTheme m_Theme;
        ushort m_ShownCores = ushort.MaxValue;

        [Inject]
        void Construct(ITankRegistry tanks, ILocalPlayer local, PlayerPalette palette, WeaponConfig weapons, CoreConfig cores, UiTheme theme)
        {
            m_Tanks = tanks; m_Local = local; m_Palette = palette; m_Weapons = weapons; m_Cores = cores; m_Theme = theme;
        }

        void LateUpdate()
        {
            if (m_Tanks == null) return;
            TankModel me = m_Tanks.Find(m_Local.TankId);
            group.alpha = me != null ? 1f : 0f;
            if (me == null) return;

            nameText.text = me.Name;
            colorPip.color = m_Palette.Get(me.ColorSlot).color;

            float hp = me.Health.Max > 0 ? me.Health.Current / (float)me.Health.Max : 0f;
            SetFill(healthFill, hp);
            healthFillImage.color = m_Theme.HealthColor(hp);
            healthText.text = me.IsAlive ? me.Health.Current + " / " + me.Health.Max : "DESTROYED";
            SetFill(shieldFill, Mathf.Clamp01(me.Status.Shield / 50f));

            WeaponConfig.Entry w = m_Weapons.Presentation(me.Weapon.WeaponId);
            weaponText.text = w.name;
            ammoText.text = me.Weapon.WeaponId == 0 ? "∞" : me.Weapon.Ammo.ToString();

            float cooldownTotal = 4f;
            float ready = me.Dash.Cooldown <= 0.05f ? 1f : 1f - Mathf.Clamp01(me.Dash.Cooldown / cooldownTotal);
            SetFill(dashFill, ready);
            dashText.text = me.Dash.Cooldown <= 0.05f ? "DASH" : me.Dash.Cooldown.ToString("0.0") + "s";

            Chip(shieldChip, shieldTime, me.Status.Shield > 0, me.Status.Shield.ToString());
            Chip(speedChip, speedTime, me.Status.SpeedTime > 0f, Mathf.CeilToInt(me.Status.SpeedTime) + "s");
            Chip(damageChip, damageTime, me.Status.DamageTime > 0f, Mathf.CeilToInt(me.Status.DamageTime) + "s");

            if (me.Cores != m_ShownCores) RebuildCores(me.Cores);
        }

        static void SetFill(RectTransform fill, float t) { fill.anchorMax = new Vector2(Mathf.Clamp01(t), fill.anchorMax.y); }

        static void Chip(GameObject chip, TMP_Text label, bool on, string text)
        {
            if (chip.activeSelf != on) chip.SetActive(on);
            if (on) label.text = text;
        }

        void RebuildCores(ushort mask)
        {
            m_ShownCores = mask;
            foreach (TMP_Text t in m_CoreEntries) Destroy(t.gameObject);
            m_CoreEntries.Clear();
            for (int i = 0; i < m_Cores.Count; i++)
            {
                if (!CoreMask.Has(mask, i)) continue;
                CoreConfig.Entry c = m_Cores.Presentation(i);
                TMP_Text entry = Instantiate(coreEntryPrefab, coreRoot);
                entry.text = c.name; entry.color = c.color;
                entry.gameObject.SetActive(true);
                m_CoreEntries.Add(entry);
            }
        }
    }
}
