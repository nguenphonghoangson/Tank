using TankGame.Prototype;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>Flags from the real map are drawn neutral for now (their capture state is not networked yet).</summary>
    public sealed class SpikeMapVisuals : MonoBehaviour
    {
        void Start()
        {
            if (Application.isBatchMode) return;
            var colors = new[] { new Color(0.3f, 0.7f, 1f), new Color(1f, 0.4f, 0.35f), new Color(0.45f, 1f, 0.5f) };
            foreach (ControlPoint cp in FindObjectsByType<ControlPoint>(FindObjectsSortMode.None)) cp.Init(colors, 10f, 45f);
        }
    }
}
