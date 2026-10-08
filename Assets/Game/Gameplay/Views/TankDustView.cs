using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>The dust a tank kicks up while driving. Its emitters emit by distance travelled, so they are silent when the tank stands; a wrecked tank smokes as well.</summary>
    public sealed class TankDustView : MonoBehaviour
    {
        [SerializeField] GameObject[] stageEmitters = new GameObject[TankDamageView.StageCount];

        public void SetStage(int stage)
        {
            for (int i = 0; i < stageEmitters.Length; i++) if (stageEmitters[i] != null) stageEmitters[i].SetActive(i == stage);
        }

        public void Show(bool visible) { gameObject.SetActive(visible); }
    }
}
