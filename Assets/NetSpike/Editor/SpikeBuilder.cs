using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankGame.NetSpike.Editor
{
    /// <summary>Builds the networking spike: the SpikeTank prefab and the NetSpike scene (built additively, so it never touches the scene that is open).</summary>
    public static class SpikeBuilder
    {
        const string Root = "Assets/NetSpike";
        const string ScenePath = "Assets/Scenes/NetSpike.unity";
        const string MatchScenePath = "Assets/Scenes/NetSpike_Match.unity";
        const string MapPrefabPath = "Assets/Prototype/Prefabs/Map_Crossfire.prefab";

        [MenuItem("Tank/Spike/Build Net Spike Scene")]
        public static void Build() { BuildScene(false); }

        [MenuItem("Tank/Spike/Build Net Spike Match Scene (real map + items + dash)")]
        public static void BuildMatch() { BuildScene(true); }

        static void BuildScene(bool useMap)
        {
            string target = useMap ? MatchScenePath : ScenePath;
            if (SceneManager.GetSceneByPath(target).IsValid() || string.IsNullOrEmpty(SceneManager.GetActiveScene().path)) EditorSceneManager.OpenScene(useMap ? ScenePath : MatchScenePath, OpenSceneMode.Single);   // the scene being rebuilt must not be open
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            if (!AssetDatabase.IsValidFolder(Root + "/Prefabs")) AssetDatabase.CreateFolder(Root, "Prefabs");

            Material ground = Lit("Ground", new Color(0.13f, 0.15f, 0.19f));
            Material wall = Lit("Wall", new Color(0.30f, 0.33f, 0.40f));
            Material block = Lit("Block", new Color(0.42f, 0.38f, 0.30f));
            Material hull = Lit("Hull", new Color(0.25f, 0.6f, 0.95f));
            Material turretMat = Lit("Turret", new Color(0.6f, 0.8f, 1f));
            Material barrelMat = Lit("Barrel", new Color(0.85f, 0.87f, 0.9f));
            Material shell = Unlit("Shell", new Color(1f, 0.9f, 0.45f));
            GameObject shellPrefab = useMap ? BuildShellPrefab() : null;
            GameObject prefab = useMap ? BuildProtoTankPrefab(shell, shellPrefab) : BuildTankPrefab(hull, turretMat, barrelMat, shell);
            GameObject pickupPrefab = null;     // built from the map's own item slot, in BuildMap

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 160f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<SpikeCamera>();
            camGo.transform.position = new Vector3(0f, 22f, -13f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -22f, 13f).normalized);

            Vector3[] mapSpawns = null, mapPickups = null;
            Vector4[] mapBlocks = null;
            if (useMap) BuildMap(out mapSpawns, out mapPickups, out pickupPrefab, out mapBlocks);
            else
            {
                // arena: 60 x 60 with walls and a few covers (same scene on server and clients, so the simulation sees the same world)
                var arena = new GameObject("Arena").transform;
                Block("Ground", arena, new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), ground);
                Block("Wall_N", arena, new Vector3(0f, 1.5f, 30.5f), new Vector3(62f, 3f, 1f), wall);
                Block("Wall_S", arena, new Vector3(0f, 1.5f, -30.5f), new Vector3(62f, 3f, 1f), wall);
                Block("Wall_E", arena, new Vector3(30.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), wall);
                Block("Wall_W", arena, new Vector3(-30.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), wall);
                Vector3[] covers = { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(-10f, 0f, 0f), new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, -10f), new Vector3(13f, 0f, 13f), new Vector3(-13f, 0f, -13f), new Vector3(13f, 0f, -13f), new Vector3(-13f, 0f, 13f) };
                for (int i = 0; i < covers.Length; i++) Block("Cover_" + i, arena, new Vector3(covers[i].x, 1f, covers[i].z), new Vector3(4f, 2f, 4f), block);
            }

            var world = new GameObject("World");
            world.AddComponent<SpikeWorld>();
            var server = world.AddComponent<SpikeServer>();
            if (useMap) { var mv = world.AddComponent<SpikeMapVisuals>(); mv.blocks = mapBlocks; mv.half = 60f; BuildFx(); }
            if (useMap)
            {
                server.spawnPoints = mapSpawns;
                server.pickupPoints = mapPickups;
                server.pickupPrefab = pickupPrefab;
                server.matchPrefab = BuildMatchPrefab();
            }

            var net = new GameObject("Network");
            var manager = net.AddComponent<NetworkManager>();
            var kcp = net.AddComponent<kcp2k.KcpTransport>();
            var lat = net.AddComponent<LatencySimulation>();
            lat.wrap = kcp;
            lat.enabled = true;
            if (!useMap) net.AddComponent<NetworkManagerHUD>();
            var boot = net.AddComponent<SpikeBootstrap>();
            manager.transport = kcp;
            manager.playerPrefab = prefab;
            if (pickupPrefab != null) manager.spawnPrefabs.Add(pickupPrefab);
            if (useMap) manager.spawnPrefabs.Add(server.matchPrefab);
            manager.autoCreatePlayer = true;
            manager.maxConnections = 8;
            manager.sendRate = 60;
            boot.manager = manager;
            boot.latencySim = lat;

            string path = useMap ? MatchScenePath : ScenePath;
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Net spike built: " + path);
        }

        /// <summary>The Crossfire map as geometry only: gameplay components are removed (flags, item slots and layout are replaced by the spike's own versions).</summary>
        static void BuildMap(out Vector3[] spawns, out Vector3[] pickups, out GameObject pickupPrefab, out Vector4[] blocks)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            var map = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(map, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Component layout = map.GetComponent("MapLayout");
            var so = new SerializedObject(layout);
            var spawnList = new System.Collections.Generic.List<Vector3>();
            SerializedProperty groups = so.FindProperty("spawnGroups");
            for (int g = 0; g < groups.arraySize; g++)
            {
                SerializedProperty pts = groups.GetArrayElementAtIndex(g).FindPropertyRelative("points");
                for (int i = 0; i < pts.arraySize; i++) spawnList.Add(((Transform)pts.GetArrayElementAtIndex(i).objectReferenceValue).position);
            }
            var pickupList = new System.Collections.Generic.List<Vector3>();
            SerializedProperty slots = so.FindProperty("pickups");
            for (int i = 0; i < slots.arraySize; i++) pickupList.Add(((Component)slots.GetArrayElementAtIndex(i).objectReferenceValue).transform.position);
            spawns = spawnList.ToArray(); pickups = pickupList.ToArray();
            var blockList = new System.Collections.Generic.List<Vector4>();
            Transform geo = map.transform.Find("Geometry");
            if (geo != null) foreach (Transform c in geo) if (c.name.StartsWith("Cover_")) blockList.Add(new Vector4(c.position.x, c.position.z, c.lossyScale.x, c.lossyScale.z));
            blocks = blockList.ToArray();

            // the item slot's own models become the networked item prefab; then strip gameplay scripts (flags keep ControlPoint for their visuals only)
            Transform slotsRoot = map.transform.Find("Pickups");
            pickupPrefab = BuildPickupPrefab(slotsRoot.GetChild(0).gameObject);
            Object.DestroyImmediate(slotsRoot.gameObject);
            foreach (MonoBehaviour mb in map.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name != "ControlPoint") Object.DestroyImmediate(mb);
            Debug.Log("Map imported: " + spawns.Length + " spawn points, " + pickups.Length + " item slots");
        }

        /// <summary>The prototype's item slot (all seven item models as children) turned into a networked item.</summary>
        static GameObject BuildPickupPrefab(GameObject slot)
        {
            var root = Object.Instantiate(slot);
            root.name = "SpikePickup";
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            root.AddComponent<NetworkIdentity>();
            var pk = root.AddComponent<SpikePickup>();
            string[] keys = { null, "Variant_Repair_", "Variant_Shield_", "Variant_Speed_", "Variant_Damage_", "Variant_Weapon_MACHINE_GUN", "Variant_Weapon_SHOTGUN", "Variant_Weapon_ROCKET" };
            pk.variantRoots = new GameObject[keys.Length];
            foreach (Transform child in root.transform)
            {
                child.gameObject.SetActive(false);
                for (int i = 1; i < keys.Length; i++) if (child.name.StartsWith(keys[i])) pk.variantRoots[i] = child.gameObject;
            }
            for (int i = 1; i < keys.Length; i++) if (pk.variantRoots[i] == null) Debug.LogError("Item model missing for " + keys[i]);

            var lab = new GameObject("Label");
            lab.transform.SetParent(root.transform, false);
            lab.transform.localPosition = new Vector3(0f, 2.9f, 0f);
            lab.transform.rotation = Quaternion.Euler(59.4f, 0f, 0f);
            var tm = lab.AddComponent<TextMesh>();
            tm.fontSize = 48; tm.characterSize = 0.12f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            lab.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            pk.label = tm;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/SpikePickup.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The prototype's projectile look (mesh and trail) without its flight and hit script.</summary>
        static GameObject BuildShellPrefab()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/Prefabs/Projectile.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "SpikeShell";
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/SpikeShell.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>The prototype's tank model (hull, tracks, turret, team ring) with all gameplay components removed.</summary>
        static GameObject BuildProtoTankPrefab(Material shell, GameObject shellPrefab)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/Prefabs/Tank_Match.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "SpikeTank";
            Transform bar = root.transform.Find("HealthBar");
            if (bar != null) Object.DestroyImmediate(bar.gameObject);
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name != "TankUnit") Object.DestroyImmediate(mb);
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            root.AddComponent<NetworkIdentity>();
            var tank = root.AddComponent<SpikeTank>();
            tank.visual = root.transform.Find("Visual");
            tank.turret = root.transform.Find("Visual/TurretPivot");

            var hp = new GameObject("HpText");
            hp.transform.SetParent(root.transform, false);
            hp.transform.localPosition = new Vector3(0f, 3.4f, 0f);
            var tm = hp.AddComponent<TextMesh>();
            tm.text = "100"; tm.fontSize = 48; tm.characterSize = 0.2f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            hp.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            hp.transform.rotation = Quaternion.Euler(59.4f, 0f, 0f);
            tank.hpText = tm;
            tank.shellMaterial = shell;
            tank.shellPrefab = shellPrefab;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/SpikeTank.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildMatchPrefab()
        {
            var go = new GameObject("SpikeMatch");
            go.AddComponent<NetworkIdentity>();
            go.AddComponent<SpikeMatch>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/SpikeMatch.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static T Load<T>(string path) where T : Component { return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<T>(); }

        /// <summary>The prototype's effect prefabs, played through its own CombatFx (muzzle, impact, hit, explosion, dash, pickup).</summary>
        static void BuildFx()
        {
            const string P = "Assets/Prototype/Prefabs/";
            var go = new GameObject("CombatFx");
            var fx = go.AddComponent<TankGame.Prototype.CombatFx>();
            fx.projectilePrefab = Load<TankGame.Prototype.Projectile>(P + "Projectile.prefab");
            fx.muzzlePrefab = Load<TankGame.Prototype.PooledFx>(P + "Fx_MuzzleFlash.prefab");
            fx.impactPrefab = Load<TankGame.Prototype.PooledFx>(P + "Fx_Impact.prefab");
            fx.hitPrefab = Load<TankGame.Prototype.PooledFx>(P + "Fx_Hit.prefab");
            fx.explosionPrefab = Load<TankGame.Prototype.PooledFx>(P + "Fx_Explosion.prefab");
            fx.pickupPrefab = Load<TankGame.Prototype.PooledFx>(P + "Fx_Pickup.prefab");
            fx.dashPrefab = Load<TankGame.Prototype.PooledFx>(P + "Fx_Dash.prefab");
        }

        static GameObject BuildTankPrefab(Material hull, Material turretMat, Material barrelMat, Material shell)
        {
            var root = new GameObject("SpikeTank");
            root.AddComponent<NetworkIdentity>();
            var tank = root.AddComponent<SpikeTank>();

            var vis = new GameObject("Visual").transform;
            vis.SetParent(root.transform, false);
            Cube("Hull", vis, new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 0.7f, 3f), hull);
            Cube("TrackL", vis, new Vector3(-1.1f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 3.3f), barrelMat);
            Cube("TrackR", vis, new Vector3(1.1f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 3.3f), barrelMat);
            var turret = new GameObject("Turret").transform;
            turret.SetParent(vis, false);
            turret.localPosition = new Vector3(0f, 1.1f, 0f);
            Cube("TurretBody", turret, new Vector3(0f, 0.15f, 0f), new Vector3(1.3f, 0.55f, 1.5f), turretMat);
            Cube("Barrel", turret, new Vector3(0f, 0.15f, 1.5f), new Vector3(0.26f, 0.26f, 2f), barrelMat);

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(flash.GetComponent<Collider>());
            flash.name = "Flash";
            flash.transform.SetParent(turret, false);
            flash.transform.localPosition = new Vector3(0f, 0.15f, 2.7f);
            flash.transform.localScale = Vector3.one * 0.9f;
            flash.GetComponent<MeshRenderer>().sharedMaterial = shell;
            flash.SetActive(false);

            var hp = new GameObject("HpText");
            hp.transform.SetParent(root.transform, false);
            hp.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            var tm = hp.AddComponent<TextMesh>();
            tm.text = "100"; tm.fontSize = 48; tm.characterSize = 0.2f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            hp.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            hp.transform.rotation = Quaternion.Euler(59.4f, 0f, 0f);

            tank.visual = vis;
            tank.turret = turret;
            tank.hpText = tm;
            tank.flash = flash;
            tank.shellMaterial = shell;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/SpikeTank.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void Block(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);          // keeps its BoxCollider
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Material Lit(string name, Color c) { return Mat(name, "Universal Render Pipeline/Lit", c); }
        static Material Unlit(string name, Color c) { return Mat(name, "Universal Render Pipeline/Unlit", c); }

        static Material Mat(string name, string shader, Color c)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
