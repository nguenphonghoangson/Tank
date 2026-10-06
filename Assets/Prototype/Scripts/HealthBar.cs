using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Billboarded world-space bar. Only visible once the tank has taken damage.</summary>
    public sealed class HealthBar : MonoBehaviour
    {
        public TankUnit unit;
        public GameObject barRoot;
        public Transform fill;
        public float width = 2.4f;

        Camera m_Cam;
        Renderer m_FillRenderer;
        MaterialPropertyBlock m_Block;

        /// <summary>Tint the fill with the team colour (the bar prefab uses a neutral material).</summary>
        public void SetColor(Color c)
        {
            if (m_FillRenderer == null) m_FillRenderer = fill.GetComponent<Renderer>();
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            m_FillRenderer.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", c);
            m_FillRenderer.SetPropertyBlock(m_Block);
        }

        void LateUpdate()
        {
            if (m_Cam == null) m_Cam = Camera.main;
            float f = unit.maxHp > 0 ? (float)unit.Hp / unit.maxHp : 0f;
            bool show = !unit.IsDead && f < 1f;
            if (barRoot.activeSelf != show) barRoot.SetActive(show);
            if (!show || m_Cam == null) return;

            barRoot.transform.rotation = m_Cam.transform.rotation;
            Vector3 s = fill.localScale;
            s.x = width * f;
            fill.localScale = s;
            Vector3 p = fill.localPosition;
            p.x = -width * (1f - f) * 0.5f;
            fill.localPosition = p;
        }
    }
}
