using UnityEngine;

namespace Tank.Gameplay.Arena
{
    /// <summary>A place a tank may appear. The arrow shows the way it will face (toward the middle of the arena).</summary>
    public sealed class SpawnPointMarker : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.95f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, 0.5f);
            Vector3 toCentre = -new Vector3(transform.position.x, 0f, transform.position.z).normalized;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.3f, transform.position + Vector3.up * 0.3f + toCentre * 2f);
        }
    }
}
