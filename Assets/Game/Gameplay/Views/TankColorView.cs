using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>The tank's player colour. It replaces the pack's player material wherever it is used on the model; the rest of the materials (treads) are left alone.</summary>
    public sealed class TankColorView : MonoBehaviour
    {
        const string PlayerMaterialPrefix = "mp_tank_player";

        [SerializeField] Renderer[] tintables = new Renderer[0];

        [ContextMenu("Collect Renderers")]
        public void CollectRenderers() { tintables = GetComponentsInChildren<MeshRenderer>(true); }

        public void Apply(Material playerMaterial)
        {
            if (playerMaterial == null) return;
            foreach (Renderer r in tintables)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.StartsWith(PlayerMaterialPrefix)) mats[i] = playerMaterial;
                r.sharedMaterials = mats;
            }
        }
    }
}
