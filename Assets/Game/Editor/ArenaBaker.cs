using System.Collections.Generic;
using System.Linq;
using Tank.Core.Hit;
using Tank.Gameplay.Arena;
using Tank.Gameplay.Config;
using UnityEditor;
using UnityEngine;

namespace Tank.Editor
{
    /// <summary>
    /// Reads the hitbox and spawn components of an arena prefab into its ArenaConfig, the data the simulation loads. Edit the gizmos in the
    /// Scene view, bake, and the rules follow; nothing about the arena is typed into a script.
    /// </summary>
    public static class ArenaBaker
    {
        public static void Bake(GameObject arenaRoot, ArenaConfig config)
        {
            ArenaAuthoring authoring = arenaRoot.GetComponent<ArenaAuthoring>();
            config.playableSemiAxes = authoring != null ? authoring.playableSemiAxes : config.playableSemiAxes;
            config.blocks = arenaRoot.GetComponentsInChildren<HitboxBlock>(true).Select(b => new ArenaConfig.Block { center = b.WorldCenter, halfExtents = b.WorldHalfExtents, yaw = b.Yaw }).ToArray();
            config.spawnPoints = arenaRoot.GetComponentsInChildren<SpawnPointMarker>(true).Select(s => new Vector2(s.transform.position.x, s.transform.position.z)).ToArray();
            config.itemSlots = arenaRoot.GetComponentsInChildren<ItemSlotMarker>(true).Select(s => new Vector2(s.transform.position.x, s.transform.position.z)).ToArray();
            EditorUtility.SetDirty(config);

            // a spawn point inside cover would trap a tank: say so now rather than in play
            var world = new HitWorld(config.BuildShapes());
            foreach (Vector2 s in config.spawnPoints)
            {
                Vector2 p = s, v = Vector2.zero; world.ResolveCircle(ref p, ref v, 1.5f);
                if ((p - s).magnitude > 0.01f) Debug.LogWarning("Spawn point " + s + " overlaps cover or the rim");
            }
        }

        [MenuItem("Tank/Game/Bake Arena Config From Selection")]
        static void BakeSelection()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null || go.GetComponentInChildren<ArenaAuthoring>(true) == null) { Debug.LogError("Select an arena prefab or an instance of one (it needs an ArenaAuthoring)."); return; }
            ArenaConfig config = AssetDatabase.LoadAssetAtPath<ArenaConfig>("Assets/Game/Content/Config/ArenaConfig.asset");
            GameObject root = go.GetComponentInChildren<ArenaAuthoring>(true).gameObject;
            if (EditorUtility.IsPersistent(go))
            {
                string path = AssetDatabase.GetAssetPath(go);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try { Bake(contents, config); } finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            else Bake(root, config);
            AssetDatabase.SaveAssets();
            Debug.Log("Arena baked: " + config.blocks.Length + " blocks, " + config.spawnPoints.Length + " spawn points");
        }
    }
}
