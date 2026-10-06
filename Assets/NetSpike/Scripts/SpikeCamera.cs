using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>Plain angled top-down follow camera for the spike.</summary>
    public sealed class SpikeCamera : MonoBehaviour
    {
        public Vector3 offset = new Vector3(0f, 22f, -13f);

        void LateUpdate()
        {
            Vector3 focus = SpikeTank.Local != null ? SpikeTank.Local.RenderPos : Vector3.zero;
            transform.position = Vector3.Lerp(transform.position, focus + offset, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.LookRotation(-offset.normalized);
        }
    }
}
