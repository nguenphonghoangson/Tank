using UnityEngine;

namespace Tank.Gameplay.Arena
{
    /// <summary>A place an item can appear. Placed under the arena's ItemSlots object and baked into the ArenaConfig with the rest.</summary>
    public sealed class ItemSlotMarker : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.95f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.4f, new Vector3(1f, 0.8f, 1f));
        }
    }
}
