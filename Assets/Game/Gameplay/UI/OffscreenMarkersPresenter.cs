using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Combat;
using Tank.Gameplay.Config;
using Tank.Gameplay.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tank.Gameplay.UI
{
    /// <summary>A coloured dot on the screen edge, toward every living tank the camera does not show, so nobody is ever lost off the side.</summary>
    public sealed class OffscreenMarkersPresenter : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] Image dotPrefab;
        [SerializeField, Range(0f, 0.2f)] float inset = 0.05f;           // how far in from the edge the dots sit, as a fraction of the screen

        readonly List<Image> m_Dots = new List<Image>();
        ITankRegistry m_Tanks; TankViewSystem m_Views; ILocalPlayer m_Local; PlayerPalette m_Palette;
        Camera m_Camera;

        [Inject]
        void Construct(ITankRegistry tanks, TankViewSystem views, ILocalPlayer local, PlayerPalette palette) { m_Tanks = tanks; m_Views = views; m_Local = local; m_Palette = palette; }

        void LateUpdate()
        {
            if (m_Tanks == null) return;
            if (m_Camera == null) m_Camera = Camera.main;
            int shown = 0;
            if (m_Camera != null)
                for (int i = 0; i < m_Tanks.All.Count; i++)
                {
                    TankModel t = m_Tanks.All[i];
                    if (t.Id == m_Local.TankId || !t.IsAlive || !m_Views.TryGetPosition(t.Id, out Vector3 world)) continue;
                    Vector3 v = m_Camera.WorldToViewportPoint(world);
                    bool inside = v.z > 0f && v.x > inset && v.x < 1f - inset && v.y > inset && v.y < 1f - inset;
                    if (inside) continue;
                    Image dot = Dot(shown++);
                    Vector2 p = new Vector2(Mathf.Clamp(v.x, inset, 1f - inset), Mathf.Clamp(v.y, inset, 1f - inset));
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = p; dot.rectTransform.anchoredPosition = Vector2.zero;
                    Color c = m_Palette.Get(t.ColorSlot).color; c.a = 0.95f; dot.color = c;
                }
            for (int i = shown; i < m_Dots.Count; i++) m_Dots[i].gameObject.SetActive(false);
        }

        Image Dot(int index)
        {
            while (m_Dots.Count <= index) m_Dots.Add(Instantiate(dotPrefab, root));
            m_Dots[index].gameObject.SetActive(true);
            return m_Dots[index];
        }
    }
}
