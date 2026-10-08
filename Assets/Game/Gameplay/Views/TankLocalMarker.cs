using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>A pulsing ring on the ground under the tank the local player drives, so it can be told from the others at any camera distance.</summary>
    public sealed class TankLocalMarker : MonoBehaviour
    {
        [SerializeField] MeshRenderer ring;
        [SerializeField, Min(0.1f)] float outerRadius = 1.9f, thickness = 0.28f;
        [SerializeField] float pulse = 0.12f, pulsesPerSecond = 1.6f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock m_Block;

        void Awake()
        {
            ring.GetComponent<MeshFilter>().sharedMesh = BuildRing(outerRadius, outerRadius - thickness, 48);
            Show(false);
        }

        public void Show(bool visible) { if (ring != null && ring.gameObject.activeSelf != visible) ring.gameObject.SetActive(visible); }

        public void SetColor(Color color)
        {
            if (ring == null) return;
            m_Block ??= new MaterialPropertyBlock();
            ring.GetPropertyBlock(m_Block);
            m_Block.SetColor(BaseColor, color);
            ring.SetPropertyBlock(m_Block);
        }

        void LateUpdate()
        {
            if (ring == null || !ring.gameObject.activeSelf) return;
            float s = 1f + pulse * Mathf.Sin(Time.time * pulsesPerSecond * Mathf.PI * 2f);
            ring.transform.localScale = new Vector3(s, 1f, s);
        }

        static Mesh BuildRing(float outer, float inner, int segments)
        {
            var vertices = new Vector3[segments * 2]; var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = dir * outer; vertices[i * 2 + 1] = dir * inner;
                int next = (i + 1) % segments, t = i * 6;
                triangles[t] = i * 2; triangles[t + 1] = i * 2 + 1; triangles[t + 2] = next * 2;          // wound so the face looks up
                triangles[t + 3] = i * 2 + 1; triangles[t + 4] = next * 2 + 1; triangles[t + 5] = next * 2;
            }
            var mesh = new Mesh { name = "LocalMarkerRing", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
