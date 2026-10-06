using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>
    /// Everything the player feels around a tank: recoil, muzzle flash, hit flash, camera and hit-stop,
    /// destruction. It only listens to TankUnit events; the simulation does not know it exists.
    /// Camera and hit-stop effects are reserved for events the local player is part of.
    /// </summary>
    public sealed class TankFeedback : MonoBehaviour
    {
        public TankUnit unit;
        public CombatFx fx;
        public Transform visualRoot;
        public Transform barrel;
        public bool isPlayer;          // legacy flag; TankUnit.isLocal is the one used by the match

        [Header("Recoil")]
        public float recoilDistance = 0.4f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color HealColor = new Color(0.45f, 1f, 0.55f);
        static readonly Color ShieldColor = new Color(0.4f, 0.9f, 1f);

        MeshRenderer[] m_Renderers;
        bool[] m_Flashable;
        Color[] m_BaseColors;
        Color m_FlashColor = Color.white, m_RingColor = Color.white;
        MaterialPropertyBlock m_Block;
        Vector3 m_BarrelRest;
        float m_Recoil, m_RecoilVel, m_Flash;
        bool m_Dirty;

        bool Local => unit.isLocal || isPlayer;

        void Awake()
        {
            m_Block = new MaterialPropertyBlock();
            m_Renderers = visualRoot.GetComponentsInChildren<MeshRenderer>();
            m_BaseColors = new Color[m_Renderers.Length];
            m_Flashable = new bool[m_Renderers.Length];
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                m_BaseColors[i] = m_Renderers[i].sharedMaterial.GetColor(BaseColorId);
                m_Flashable[i] = m_Renderers[i].name != "TeamRing";
            }
            m_BarrelRest = barrel.localPosition;
        }

        void OnEnable()
        {
            unit.Fired += OnFired;
            unit.Damaged += OnDamaged;
            unit.Healed += OnHealed;
            unit.Died += OnDied;
            unit.Respawned += OnRespawned;
            unit.SkillUsed += OnSkillUsed;
            unit.ItemUsed += OnItemUsed;
        }

        void OnDisable()
        {
            unit.Fired -= OnFired;
            unit.Damaged -= OnDamaged;
            unit.Healed -= OnHealed;
            unit.Died -= OnDied;
            unit.Respawned -= OnRespawned;
            unit.SkillUsed -= OnSkillUsed;
            unit.ItemUsed -= OnItemUsed;
        }

        /// <summary>Team colours: hull and turret get the team colour, the ring under the tank shows it from far away.</summary>
        public void SetTint(Color hull, Color turret, Color ring)
        {
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                string n = m_Renderers[i].name;
                if (n == "Hull") m_BaseColors[i] = hull;
                else if (n == "Turret") m_BaseColors[i] = turret;
                else if (n == "TeamRing") m_BaseColors[i] = ring;
            }
            m_RingColor = ring;
            m_Dirty = true;
            ApplyColors(0f);
        }

        void OnFired(TankUnit u)
        {
            Vector3 dir = u.muzzle.forward;
            fx.SpawnMuzzle(u.muzzle.position, dir);
            m_Recoil = recoilDistance;
            m_RecoilVel = 0f;
            // per-shot camera feedback scales with how slow the weapon is: a machine gun must not shake the screen 12 times a second
            float k = Mathf.Clamp(u.weapon.fireInterval / 0.33f, 0.12f, 1.6f);
            if (Local && fx.cameraRig != null)
            {
                fx.cameraRig.Kick(-dir, 0.35f * k);
                fx.cameraRig.AddTrauma(0.12f * k);
            }
            else fx.ShakeAt(u.muzzle.position, 0.05f);
        }

        void OnDamaged(TankUnit u, int damage, Vector3 point, Vector3 dir)
        {
            StartFlash(damage <= 0 || u.ShieldHp > 0 ? ShieldColor : Color.white);
            bool attackerLocal = u.LastAttacker != null && u.LastAttacker.isLocal;
            // shake scales with the damage dealt; freezing is kept for kills only (see OnDied)
            float w = Mathf.Clamp(damage / 25f, 0.1f, 1.5f);
            if (Local) fx.Shake(0.4f * w);
            else if (attackerLocal) fx.Shake(0.15f * w);
            else fx.ShakeAt(point, 0.07f * w);
        }

        void OnItemUsed(TankUnit u, Color c)
        {
            StartFlash(Color.Lerp(c, Color.white, 0.25f));
        }

        void OnHealed(TankUnit u, int amount)
        {
            StartFlash(HealColor);
        }

        void OnDied(TankUnit u)
        {
            Vector3 p = u.transform.position + Vector3.up * 0.8f;
            fx.SpawnExplosion(p);
            bool killerLocal = u.Killer != null && u.Killer.isLocal;
            if (Local || killerLocal)
            {
                fx.Shake(Local ? 1f : 0.85f);
                fx.HitStop(Local ? 0.09f : 0.07f);
            }
            else fx.ShakeAt(p, 0.55f);
            visualRoot.gameObject.SetActive(false);
            ClearFlash();
        }

        void OnRespawned(TankUnit u)
        {
            visualRoot.gameObject.SetActive(true);
            visualRoot.localScale = Vector3.one;
            ClearFlash();
        }

        void OnSkillUsed(TankUnit u)
        {
            Vector3 dir = u.LastDashDirection;
            fx.SpawnDash(u.transform.position + Vector3.up * 0.4f, -dir);
            if (Local && fx.cameraRig != null) fx.cameraRig.Kick(dir, 0.7f);
        }

        void StartFlash(Color c)
        {
            m_FlashColor = c;
            m_Flash = 1f;
        }

        void Update()
        {
            // barrel recoil: spring back to rest (critically damped, ~0.15 s)
            if (m_Recoil != 0f || m_RecoilVel != 0f)
            {
                float dt = Time.deltaTime;
                m_RecoilVel += (-m_Recoil * 700f - m_RecoilVel * 53f) * dt;
                m_Recoil += m_RecoilVel * dt;
                if (Mathf.Abs(m_Recoil) < 0.001f && Mathf.Abs(m_RecoilVel) < 0.01f) { m_Recoil = 0f; m_RecoilVel = 0f; }
                barrel.localPosition = m_BarrelRest - new Vector3(0f, 0f, m_Recoil);
            }

            if (m_Flash > 0f)
            {
                m_Flash = Mathf.Max(0f, m_Flash - Time.unscaledDeltaTime / 0.12f);
                ApplyColors(m_Flash);
                visualRoot.localScale = Vector3.one * (1f + 0.08f * m_Flash);
                m_Dirty = true;
                if (m_Flash == 0f) ClearFlash();
            }
        }

        void ApplyColors(float flash)
        {
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                m_Renderers[i].GetPropertyBlock(m_Block);
                m_Block.SetColor(BaseColorId, m_Flashable[i] ? Color.Lerp(m_BaseColors[i], m_FlashColor, flash) : m_BaseColors[i]);
                m_Renderers[i].SetPropertyBlock(m_Block);
            }
        }

        void ClearFlash()
        {
            m_Flash = 0f;
            if (!m_Dirty) return;
            m_Dirty = false;
            ApplyColors(0f);
            visualRoot.localScale = Vector3.one;
        }
    }
}
