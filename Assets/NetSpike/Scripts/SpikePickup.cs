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
        public enum Kind : byte { None = 0, Repair = 1, Shield = 2, Speed = 3, Damage = 4, MachineGun = 5, Shotgun = 6, Rocket = 7 }
        public const float Radius = 2.2f;
        static readonly string[] Labels = { "", "REPAIR", "SHIELD", "SPEED", "DAMAGE x1.5", "MACHINE GUN", "SHOTGUN", "ROCKET" };
        static readonly Color[] Colors = { Color.clear, new Color(0.35f, 1f, 0.5f), new Color(0.3f, 0.85f, 1f), new Color(1f, 0.9f, 0.25f), new Color(1f, 0.3f, 0.25f), new Color(1f, 0.65f, 0.15f), new Color(0.95f, 0.35f, 0.9f), new Color(0.7f, 0.5f, 1f) };
        static readonly float[] Weights = { 0f, 3f, 2f, 2f, 1.5f, 1.5f, 1.5f, 1f };
        static readonly float[] Respawn = { 0f, 25f, 30f, 30f, 40f, 35f, 35f, 45f };

        [SyncVar(hook = nameof(OnVariant))] public byte variant;
        public Renderer orb;
        public TextMesh label;

        float m_RespawnAt;
        MaterialPropertyBlock m_Block;

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
            bool on = variant != 0;
            if (orb != null) orb.gameObject.SetActive(on);
            if (label != null) { label.gameObject.SetActive(on); label.text = Labels[variant]; }
            if (on && orb != null)
            {
                if (m_Block == null) m_Block = new MaterialPropertyBlock();
                orb.GetPropertyBlock(m_Block);
                m_Block.SetColor("_BaseColor", Colors[variant]); m_Block.SetColor("_Color", Colors[variant]);
                orb.SetPropertyBlock(m_Block);
            }
        }

        void Update()
        {
            if (variant == 0 || orb == null || Application.isBatchMode) return;
            orb.transform.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
            orb.transform.localPosition = new Vector3(0f, 1.1f + 0.15f * Mathf.Sin(Time.time * 3f), 0f);
        }
    }
}
