using System;
using UnityEngine;

namespace TankGame.Prototype
{
    public enum PickupKind { Repair, Shield, Speed, Damage, Weapon }

    /// <summary>One possible item for a slot: its look (a child object built by the map) and its effect.</summary>
    [Serializable]
    public sealed class PickupVariant
    {
        public PickupKind kind;
        public string label;
        public Color color;
        public WeaponDef weapon;
        public float weight = 1f;
        public float respawnSeconds = 25f;
        public GameObject root;
        public Transform visual;
    }

    /// <summary>
    /// An item slot on the map. Each time it (re)appears it rolls a random item from its variants (weighted), so every
    /// match and every respawn can differ. Drive over it to use it; a repair kit stays until someone actually needs it.
    /// </summary>
    public sealed class Pickup : MonoBehaviour
    {
        public PickupVariant[] variants;

        [Header("Effect values")]
        public int healAmount = 40;
        public int shieldAmount = 50;
        public float shieldSeconds = 12f;
        public float speedMultiplier = 1.4f;
        public float speedSeconds = 8f;
        public float damageMultiplier = 1.5f;
        public float damageSeconds = 10f;

        public CombatFx fx;
        public float respawnScale = 1f;

        // current item (set by Roll)
        public PickupKind kind { get; private set; }
        public string label { get; private set; } = "";
        public Color color { get; private set; } = Color.white;
        public WeaponDef weapon { get; private set; }
        public bool Available { get; private set; }

        public event Action<Pickup, TankUnit> Collected;

        System.Random m_Rng;
        PickupVariant m_Current;
        Transform m_Visual;
        Vector3 m_VisualRest;
        float m_RespawnAt, m_RespawnSeconds = 25f;

        /// <summary>Hide the item. With active == false the slot stays empty for a short random while, then rolls.</summary>
        public void Roll(System.Random rng, bool active)
        {
            m_Rng = rng ?? m_Rng ?? new System.Random();
            foreach (PickupVariant v in variants) v.root.SetActive(false);
            m_Current = null;
            Available = false;
            if (!active) { m_RespawnAt = Time.time + 6f + (float)m_Rng.NextDouble() * 18f; return; }

            float total = 0f;
            foreach (PickupVariant v in variants) total += v.weight;
            float r = (float)m_Rng.NextDouble() * total;
            PickupVariant pick = variants[variants.Length - 1];
            foreach (PickupVariant v in variants) { if (r < v.weight) { pick = v; break; } r -= v.weight; }

            m_Current = pick;
            kind = pick.kind; label = pick.label; color = pick.color; weapon = pick.weapon; m_RespawnSeconds = pick.respawnSeconds;
            m_Visual = pick.visual;
            m_VisualRest = m_Visual.localPosition;
            pick.root.SetActive(true);
            Available = true;
        }

        /// <summary>Test/debug: make this slot show a specific kind right now. Returns false if the slot has no such variant.</summary>
        public bool Force(PickupKind wanted)
        {
            foreach (PickupVariant v in variants)
            {
                if (v.kind != wanted) continue;
                foreach (PickupVariant o in variants) o.root.SetActive(false);
                m_Current = v;
                kind = v.kind; label = v.label; color = v.color; weapon = v.weapon; m_RespawnSeconds = v.respawnSeconds;
                m_Visual = v.visual; m_VisualRest = m_Visual.localPosition;
                v.root.SetActive(true);
                Available = true;
                return true;
            }
            return false;
        }

        void Update()
        {
            if (!Available)
            {
                if (variants != null && variants.Length > 0 && Time.time >= m_RespawnAt) Roll(m_Rng, true);
                return;
            }
            if (m_Visual != null)
            {
                m_Visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.Self);
                m_Visual.localPosition = m_VisualRest + new Vector3(0f, 0.15f * Mathf.Sin(Time.time * 3f), 0f);
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (!Available) return;
            TankUnit t = other.GetComponentInParent<TankUnit>();
            if (t == null || t.IsDead) return;
            if (!Apply(t)) return;

            PickupVariant used = m_Current;
            Available = false;
            m_RespawnAt = Time.time + m_RespawnSeconds * respawnScale * (0.8f + 0.4f * (float)m_Rng.NextDouble());
            used.root.SetActive(false);
            if (fx != null) fx.SpawnPickup(transform.position + Vector3.up * 0.6f);
            Collected?.Invoke(this, t);
        }

        /// <summary>Returns false when the item would be wasted.</summary>
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
