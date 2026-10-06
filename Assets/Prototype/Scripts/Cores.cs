using System;
using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Stat changes a tank has accumulated from cores. Multipliers start at 1, bonuses at 0.</summary>
    [Serializable]
    public sealed class TankModifiers
    {
        public int maxHpBonus;
        public float speedMult = 1f, damageMult = 1f, fireIntervalMult = 1f, reloadMult = 1f, magazineMult = 1f;
        public float dashCooldownMult = 1f, captureMult = 1f, killScoreMult = 1f;
        public float lifesteal, regenPerSecond, splashBonus;

        public void Add(TankModifiers d)
        {
            maxHpBonus += d.maxHpBonus;
            speedMult *= d.speedMult; damageMult *= d.damageMult; fireIntervalMult *= d.fireIntervalMult;
            reloadMult *= d.reloadMult; magazineMult *= d.magazineMult; dashCooldownMult *= d.dashCooldownMult;
            captureMult *= d.captureMult; killScoreMult *= d.killScoreMult;
            lifesteal += d.lifesteal; regenPerSecond += d.regenPerSecond; splashBonus += d.splashBonus;
        }
    }

    /// <summary>One selectable core: a named bundle of stat changes (some with a trade-off).</summary>
    public sealed class CoreDef
    {
        public string id, name, description;
        public Color color;
        public TankModifiers delta;
    }

    /// <summary>The pool the per-phase offers are drawn from. Data only; add a core by adding a line.</summary>
    public static class CoreLibrary
    {
        public static readonly CoreDef[] All =
        {
            Make("plating", "Heavy Plating", "+40 max HP, 8% slower", new Color(0.55f, 0.75f, 1f), new TankModifiers { maxHpBonus = 40, speedMult = 0.92f }),
            Make("overdrive", "Overdrive Engine", "+18% move speed", new Color(1f, 0.85f, 0.3f), new TankModifiers { speedMult = 1.18f }),
            Make("heavy", "Heavy Shells", "+30% damage, 15% slower fire", new Color(1f, 0.5f, 0.3f), new TankModifiers { damageMult = 1.3f, fireIntervalMult = 1.15f }),
            Make("rapid", "Rapid Loader", "22% faster fire, -10% damage", new Color(1f, 0.95f, 0.5f), new TankModifiers { fireIntervalMult = 0.78f, damageMult = 0.9f }),
            Make("hands", "Quick Hands", "40% faster reload, +40% magazine", new Color(0.7f, 1f, 0.7f), new TankModifiers { reloadMult = 0.6f, magazineMult = 1.4f }),
            Make("blast", "Blast Rounds", "Shells explode: +1.8 m splash", new Color(1f, 0.6f, 0.2f), new TankModifiers { splashBonus = 1.8f }),
            Make("after", "Afterburner", "Dash cooldown -45%", new Color(0.6f, 0.9f, 1f), new TankModifiers { dashCooldownMult = 0.55f }),
            Make("nano", "Repair Nanites", "Regenerate 4 HP/s out of combat", new Color(0.45f, 1f, 0.6f), new TankModifiers { regenPerSecond = 4f }),
            Make("zone", "Zone Engineer", "Capture and fortify flags 50% faster", new Color(0.4f, 0.8f, 1f), new TankModifiers { captureMult = 1.5f }),
            Make("bounty", "Bounty Hunter", "Kill and assist score +50%", new Color(1f, 0.8f, 0.4f), new TankModifiers { killScoreMult = 1.5f }),
            Make("vamp", "Vampiric Rounds", "Heal 25% of damage dealt", new Color(1f, 0.4f, 0.5f), new TankModifiers { lifesteal = 0.25f }),
            Make("glass", "Glass Cannon", "+50% damage, -25 max HP", new Color(1f, 0.35f, 0.35f), new TankModifiers { damageMult = 1.5f, maxHpBonus = -25 }),
        };

        static CoreDef Make(string id, string name, string description, Color color, TankModifiers delta)
        {
            return new CoreDef { id = id, name = name, description = description, color = color, delta = delta };
        }
    }
}
