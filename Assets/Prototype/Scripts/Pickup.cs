using UnityEngine;

namespace TankGame.Prototype
{
    public enum PickupKind { Repair, Shield, Speed, Damage, Weapon }

    /// <summary>
    /// Item lying on the map: drive over it to use it. Kinds: Repair (heal), Shield (absorbs damage), Speed boost,
    /// Damage boost, and Weapon (a special weapon with limited shots). Respawns after a while.
    /// </summary>
    public sealed class Pickup : MonoBehaviour
    {
        public PickupKind kind = PickupKind.Repair;
        public string label = "REPAIR";
        public Color color = new Color(0.35f, 1f, 0.5f);
        public float respawnSeconds = 25f;

        [Header("Effect values")]
        public int healAmount = 40;
        public int shieldAmount = 50;
        public float shieldSeconds = 12f;
        public float speedMultiplier = 1.4f;
        public float speedSeconds = 8f;
        public float damageMultiplier = 1.5f;
        public float damageSeconds = 10f;
        public WeaponDef weapon;

        public Transform visual;
        public CombatFx fx;

        public bool Available { get; private set; } = true;
        public event System.Action<Pickup, TankUnit> Collected;

        float m_RespawnAt;
        Vector3 m_VisualRest;

        void Awake() { if (visual != null) m_VisualRest = visual.localPosition; }

        public void ResetPickup()
        {
            Available = true;
            if (visual != null) visual.gameObject.SetActive(true);
        }

        void Update()
        {
            if (!Available)
            {
                if (Time.time >= m_RespawnAt) ResetPickup();
                return;
            }
            if (visual != null)
            {
                visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.Self);
                visual.localPosition = m_VisualRest + new Vector3(0f, 0.15f * Mathf.Sin(Time.time * 3f), 0f);
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (!Available) return;
            TankUnit t = other.GetComponentInParent<TankUnit>();
            if (t == null || t.IsDead) return;
            if (!Apply(t)) return;

            Available = false;
            m_RespawnAt = Time.time + respawnSeconds;
            if (visual != null) visual.gameObject.SetActive(false);
            if (fx != null) fx.SpawnPickup(transform.position + Vector3.up * 0.6f);
            Collected?.Invoke(this, t);
        }

        /// <summary>Returns false when the item would be wasted (a repair kit stays until someone needs it).</summary>
        bool Apply(TankUnit t)
        {
            switch (kind)
            {
                case PickupKind.Repair: return t.Heal(healAmount) > 0;
                case PickupKind.Shield: t.GiveShield(shieldAmount, shieldSeconds, color); return true;
                case PickupKind.Speed: t.GiveSpeed(speedMultiplier, speedSeconds, color); return true;
                case PickupKind.Damage: t.GiveDamage(damageMultiplier, damageSeconds, color); return true;
                case PickupKind.Weapon:
                    if (weapon == null || t.weapon == weapon) return false;
                    t.GrantWeapon(weapon);
                    return true;
            }
            return false;
        }
    }
}
