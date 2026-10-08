using Tank.Gameplay.Views;
using UnityEngine;

namespace Tank.Gameplay.Arena
{
    /// <summary>
    /// A solid rectangle on the ground: rocks, pillars, walls. It lives on its own object under the arena's Hitboxes, apart from the
    /// meshes you see, so moving or resizing a hitbox never touches the art. Drawn as a gizmo; the yaw is the object's yaw.
    /// </summary>
    public sealed class HitboxBlock : MonoBehaviour
    {
        public Vector2 halfExtents = new Vector2(1f, 1f);
        public Vector2 centerOffset;

        public Vector2 WorldCenter { get { Vector3 c = transform.TransformPoint(new Vector3(centerOffset.x, 0f, centerOffset.y)); return new Vector2(c.x, c.z); } }
        public Vector2 WorldHalfExtents => new Vector2(halfExtents.x * Mathf.Abs(transform.lossyScale.x), halfExtents.y * Mathf.Abs(transform.lossyScale.z));
        public float Yaw => transform.eulerAngles.y;

        void OnDrawGizmos() { GizmoShapes.Box(transform, new Vector3(centerOffset.x, 0f, centerOffset.y), halfExtents, new Color(1f, 0.35f, 0.3f, 0.95f)); }
    }
}
