using Tank.Gameplay.Views;
using UnityEngine;

namespace Tank.Gameplay.Arena
{
    /// <summary>The root of an arena prefab: the playable ellipse (the rim). Its blocks and spawn points are the child components; the ArenaConfig is baked from all of them.</summary>
    public sealed class ArenaAuthoring : MonoBehaviour
    {
        [Tooltip("Half-width and half-depth of the area tanks may drive in, measured on the inside of the rim.")]
        public Vector2 playableSemiAxes = new Vector2(53f, 35.5f);

        void OnDrawGizmos() { GizmoShapes.Ellipse(transform.position, playableSemiAxes, new Color(1f, 0.85f, 0.2f, 0.9f)); }
    }
}
