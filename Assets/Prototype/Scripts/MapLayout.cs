using System;
using UnityEngine;

namespace TankGame.Prototype
{
    [Serializable]
    public sealed class SpawnGroup
    {
        public Transform[] points;
    }

    /// <summary>
    /// Everything gameplay needs to know about a map. A map is a prefab with one MapLayout; the match code never
    /// references a specific map, so a new map is a new prefab (plus a MapDef asset), not new code.
    /// </summary>
    public sealed class MapLayout : MonoBehaviour
    {
        public string mapName = "Map";
        public float arenaHalfSize = 28f;
        public SpawnGroup[] spawnGroups;
        public ControlPoint[] controlPoints;
        public Pickup[] pickups;
        public Transform[] minimapBlocks;     // walls and cover, drawn on the minimap by position and scale
    }
}
