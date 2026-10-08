using System;
using UnityEngine;

namespace Tank.Gameplay.Config
{
    /// <summary>Per-slot player identity: name, UI colour and the tank material. A tank takes the first free slot when it joins.</summary>
    [CreateAssetMenu(menuName = "Tank/Config/Player Palette", fileName = "PlayerPalette")]
    public sealed class PlayerPalette : ScriptableObject
    {
        [Serializable]
        public struct Slot { public string name; public Color color; public Material tankMaterial; }

        public Slot[] slots = new Slot[0];
        public int Count => slots.Length;
        public Slot Get(int index) { return slots[((index % slots.Length) + slots.Length) % slots.Length]; }
    }
}
