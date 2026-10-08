using System;
using System.Collections.Generic;
using Tank.Core.Hit;
using UnityEngine;

namespace Tank.Gameplay.Config
{
    /// <summary>The arena as data: the rim, the cover blocks and the spawn points. Baked from the arena prefab by the content builder, read by the hit world.</summary>
    [CreateAssetMenu(menuName = "Tank/Config/Arena", fileName = "ArenaConfig")]
    public sealed class ArenaConfig : ScriptableObject
    {
        [Serializable]
        public struct Block { public Vector2 center; public Vector2 halfExtents; public float yaw; }

        public Vector2 playableSemiAxes = new Vector2(54f, 36f);
        public Block[] blocks = new Block[0];
        public Vector2[] spawnPoints = new Vector2[0];
        public Vector2[] itemSlots = new Vector2[0];

        public IEnumerable<IHitShape> BuildShapes()
        {
            yield return new EllipseBoundary(playableSemiAxes.x, playableSemiAxes.y);
            foreach (Block b in blocks) yield return new ObbShape(b.center, b.halfExtents, b.yaw);
        }
    }
}
