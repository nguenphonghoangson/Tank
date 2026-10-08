using Mirror;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>
    /// An item slot. The server rolls which item it holds (random, weighted) and respawns it after a timer; clients only see the synced
    /// variant. Collection is decided by the server from the authoritative tank position.
    /// </summary>
    public sealed class SpikePickup : NetworkBehaviour
    {
        public enum Kind : byte { None = 0, Repair = 1, Shield = 2, Speed = 3, Damage = 4, MachineGun = 5, Shotgun = 6, Rocket = 7, Gigavolt = 8, Grenade = 9 }
        public const float Radius = 2.2f;
        static readonly string[] Labels = { "", "REPAIR", "SHIELD", "SPEED", "DAMAGE x1.5", "MACHINE GUN", "SHOTGUN", "ROCKET", "GIGAVOLT", "GRENADE" };
        static readonly Color[] Colors = { Color.clear, new Color(0.35f, 1f, 0.5f), new Color(0.3f, 0.85f, 1f), new Color(1f, 0.9f, 0.25f), new Color(1f, 0.3f, 0.25f), new Color(1f, 0.65f, 0.15f), new Color(0.95f, 0.35f, 0.9f), new Color(0.7f, 0.5f, 1f), new Color(0.4f, 0.8f, 1f), new Color(0.6f, 0.9f, 0.3f) };
        static readonly float[] Weights = { 0f, 3f, 2f, 2f, 1.5f, 1.5f, 1.5f, 1f, 1f, 1f };
        static readonly float[] Respawn = { 0f, 25f, 30f, 30f, 40f, 35f, 35f, 45f, 40f, 40f };

        [SyncVar(hook = nameof(OnVariant))] public byte variant;
        public GameObject[] variantRoots;     // indexed by variant (the prototype's own item models, one child per kind)
        public TextMesh label;

        float m_RespawnAt;

        public bool Available => variant != 0;
        public Kind CurrentKind => (Kind)variant;

        // ---- server
        public void ServerRoll(System.Random rng, bool active)
        {
            if (!active) { variant = 0; return; }
            float total = 0f; for (int i = 1; i < Weights.Length; i++) total += Weights[i];
            float r = (float)rng.NextDouble() * total;
            int pick = Weights.Length - 1;
            for (int i = 1; i < Weights.Length; i++) { if (r < Weights[i]) { pick = i; break; } r -= Weights[i]; }
            variant = (byte)pick;
        }

        public void ServerTake(float now, System.Random rng, float scale)
        {
            float secs = Respawn[variant] * scale * (0.8f + 0.4f * (float)rng.NextDouble());
            variant = 0;
            m_RespawnAt = now + secs;
        }

        public void ServerTick(float now, System.Random rng)
        {
            if (variant == 0 && now >= m_RespawnAt) ServerRoll(rng, true);
        }

        // ---- client
        void Awake() { ApplyVariant(); }
        public override void OnStartClient() { ApplyVariant(); }
        void OnVariant(byte oldV, byte newV) { ApplyVariant(); }

        void ApplyVariant()
        {
            if (variantRoots != null)
                for (int i = 1; i < variantRoots.Length; i++) if (variantRoots[i] != null) variantRoots[i].SetActive(i == variant);
            if (label != null) { label.gameObject.SetActive(variant != 0); label.text = Labels[variant]; }
            m_Visual = variant != 0 && variantRoots != null && variantRoots[variant] != null ? variantRoots[variant].transform.Find("Visual") : null;
            if (m_Visual != null) m_VisualRest = m_Visual.localPosition;
            if (m_ShownVariant != 0 && variant == 0 && !Application.isBatchMode)
            {
                var fx = Object.FindFirstObjectByType<TankGame.Prototype.CombatFx>();
                if (fx != null) fx.SpawnPickup(transform.position + Vector3.up * 0.6f);
            }
            m_ShownVariant = variant;
        }

        Transform m_Visual; Vector3 m_VisualRest; byte m_ShownVariant;

        void Update()
        {
            if (m_Visual == null || Application.isBatchMode) return;
            m_Visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.Self);
            m_Visual.localPosition = m_VisualRest + new Vector3(0f, 0.15f * Mathf.Sin(Time.time * 3f), 0f);
        }
    }
}
