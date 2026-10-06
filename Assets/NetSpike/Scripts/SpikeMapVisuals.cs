using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>Map data the screen needs for the minimap (cover blocks as x, z, size x, size z), filled in by the builder.</summary>
    public sealed class SpikeMapVisuals : MonoBehaviour
    {
        public static SpikeMapVisuals I { get; private set; }
        public Vector4[] blocks = new Vector4[0];
        public float half = 60f;
        void Awake() { I = this; }
    }
}
