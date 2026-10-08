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
            GameObject[] shellOverrides = useMap ? new[] { BuildPackShell("shot_default_visual"), BuildPackShell("shot_minigun_visual"), null, null, null, BuildPackShell("shot_grenade_visual") } : null;
            GameObject prefab = useMap ? BuildProtoTankPrefab(shell, shellPrefab, shellOverrides) : BuildTankPrefab(hull, turretMat, barrelMat, shell);
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
            if (useMap) BuildArenaMap(out mapSpawns, out mapPickups, out pickupPrefab, out mapBlocks);
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

        const string ModelDir = "Assets/Models/";
        const float OvalA = 27f * MapScale, OvalB = 18.1f * MapScale;      // barrier_oval01 wall centre line (semi-axes); the play area is just inside it
        const float TankScale = 1.8f;                // the pack tank (hull 1.4 x 1.6 m) is drawn at this scale to match the simulation's footprint
        const float MapScale = 2f;                   // the pack's arena is sized for its own small tank; ours is about twice that, so the arena grows with it
        const float CoverScale = 1.5f;
        const float FlagRadius = 9f;

        /// <summary>
        /// The laser arena from the new pack: platform as ground, oval rim as the visual wall, and every collision shape built here as plain boxes
        /// (a ring around the oval, one per cover) because the simulation's ComputePenetration cannot use a concave MeshCollider.
        /// Flags and the item model are taken from the old Crossfire prefab (their visuals are reused, positions are new); its geometry is dropped.
        /// </summary>
        static void BuildArenaMap(out Vector3[] spawns, out Vector3[] pickups, out GameObject pickupPrefab, out Vector4[] blocks)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            var old = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(old, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            pickupPrefab = BuildPickupPrefab(old.transform.Find("Pickups").GetChild(0).gameObject);

            var map = new GameObject("Map_LaserArena").transform;
            var flags = new GameObject("Flags").transform;
            flags.SetParent(map, false);
            Vector3[] flagPos = { new Vector3(-32f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(32f, 0f, 0f) };
            string[] flagKey = { "Point_A", "Point_B", "Point_C" };
            for (int i = 0; i < flagKey.Length; i++)
            {
                Transform f = old.transform.Find("ControlPoints/" + flagKey[i]);
                f.SetParent(flags, false);
                f.position = flagPos[i];
                f.localScale *= FlagRadius / 8f;                        // the old visuals were sized for radius 8
                f.GetComponent<TankGame.Prototype.ControlPoint>().radius = FlagRadius;
            }
            foreach (MonoBehaviour mb in old.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name != "ControlPoint") Object.DestroyImmediate(mb);
            Object.DestroyImmediate(old);

            var geo = new GameObject("Geometry").transform;
            geo.SetParent(map, false);
            Visual(ModelDir + "laser_arenaplatform_combined.fbx", "platform01_combined", "Platform", geo, new Vector3(1.1f, 0f, -0.2f) * MapScale, "Assets/Materials/Special/mg_LaserGround.mat").transform.localScale = Vector3.one * MapScale;   // top face at y 0, centred on the oval
            Visual(ModelDir + "barrieroval01.fbx", "barrier_oval01", "Rim", geo, Vector3.zero, "Assets/Materials/FX/mfx_forceField.mat").transform.localScale = Vector3.one * MapScale;

            // lava far below the platform, as in the pack's own levels (visual only)
            var lava = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(lava.GetComponent<Collider>());
            lava.name = "Lava";
            lava.transform.SetParent(geo, false);
            lava.transform.SetPositionAndRotation(new Vector3(0f, -4.2f, 0f), Quaternion.Euler(90f, 0f, 0f));
            lava.transform.localScale = new Vector3(280f, 280f, 1f);
            lava.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FX/mfx_LaserLava.mat");

            // wall: a ring of boxes along the oval
            const int N = 48;
            var wall = new GameObject("Wall").transform;
            wall.SetParent(geo, false);
            for (int i = 0; i < N; i++)
            {
                float t0 = i * Mathf.PI * 2f / N, t1 = (i + 1) * Mathf.PI * 2f / N;
                Vector3 p0 = new Vector3(OvalA * Mathf.Cos(t0), 0f, OvalB * Mathf.Sin(t0)), p1 = new Vector3(OvalA * Mathf.Cos(t1), 0f, OvalB * Mathf.Sin(t1));
                var seg = new GameObject("Wall_" + i);
                seg.transform.SetParent(wall, false);
                seg.transform.position = (p0 + p1) * 0.5f + Vector3.up * 2f;
                seg.transform.rotation = Quaternion.LookRotation((p1 - p0).normalized);
                var bc = seg.AddComponent<BoxCollider>();
                bc.size = new Vector3(2.4f, 4f, (p1 - p0).magnitude * 1.15f);
            }

            // cover: (model, x, z, yaw), mirrored left/right and front/back so neither side is favoured
            var cover = new GameObject("Cover").transform;
            cover.SetParent(geo, false);
            var blockList = new System.Collections.Generic.List<Vector4>();
            var defs = new[]
            {
                new CoverDef("lava_rock01", 8f, 6f, 20f), new CoverDef("lava_rock01", -8f, -6f, 20f), new CoverDef("lava_rock01", 8f, -6f, -20f), new CoverDef("lava_rock01", -8f, 6f, -20f),
                new CoverDef("lava_rock04", 16f, 10f, 0f), new CoverDef("lava_rock04", -16f, -10f, 0f), new CoverDef("lava_rock04", 16f, -10f, 90f), new CoverDef("lava_rock04", -16f, 10f, 90f),
                new CoverDef("lava_pillar01", 3f, 8f, 0f), new CoverDef("lava_pillar02", -3f, 8f, 0f), new CoverDef("lava_pillar01", -3f, -8f, 0f), new CoverDef("lava_pillar02", 3f, -8f, 0f),
            };
            for (int i = 0; i < defs.Length; i++)
            {
                GameObject c = Visual(ModelDir + "laser_blockers.fbx", defs[i].model, "Cover_" + i, cover, new Vector3(defs[i].x, 0f, defs[i].z) * MapScale);
                c.transform.rotation = Quaternion.Euler(0f, defs[i].yaw, 0f);
                c.transform.localScale = Vector3.one * CoverScale;
                var mf = c.GetComponent<MeshFilter>();
                var bc = c.AddComponent<BoxCollider>();
                bc.center = mf.sharedMesh.bounds.center; bc.size = mf.sharedMesh.bounds.size;
                Bounds wb = c.GetComponent<Renderer>().bounds;
                blockList.Add(new Vector4(wb.center.x, wb.center.z, wb.size.x, wb.size.z));
            }
            blocks = blockList.ToArray();

            spawns = new[]
            {
                new Vector3(-21f, 0f, -6f), new Vector3(-21f, 0f, 6f), new Vector3(21f, 0f, -6f), new Vector3(21f, 0f, 6f),
                new Vector3(-6f, 0f, -14f), new Vector3(6f, 0f, -14f), new Vector3(-6f, 0f, 14f), new Vector3(6f, 0f, 14f),
            };
            pickups = new[]
            {
                new Vector3(-8f, 0f, -11f), new Vector3(8f, 0f, 11f), new Vector3(8f, 0f, -11f), new Vector3(-8f, 0f, 11f),
                new Vector3(-23f, 0f, 0f), new Vector3(23f, 0f, 0f), new Vector3(0f, 0f, -13f), new Vector3(0f, 0f, 13f),
            };
            for (int i = 0; i < spawns.Length; i++) spawns[i] *= MapScale;
            for (int i = 0; i < pickups.Length; i++) pickups[i] *= MapScale;
            Debug.Log("Laser arena built: " + spawns.Length + " spawn points, " + pickups.Length + " item slots, " + defs.Length + " cover blocks, " + flagKey.Length + " flags");
        }

        struct CoverDef
        {
            public string model; public float x, z, yaw;
            public CoverDef(string model, float x, float z, float yaw) { this.model = model; this.x = x; this.z = z; this.yaw = yaw; }
        }

        /// <summary>One named mesh of a pack model, instanced as its own object (no collider, no scripts).</summary>
        static GameObject Visual(string fbxPath, string childName, string name, Transform parent, Vector3 pos, string materialPath = "Assets/Materials/Palettes/mp_LaserTheme.mat")
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            Transform part = null;
            foreach (Transform t in fbx.GetComponentsInChildren<Transform>(true)) if (t.name == childName) { part = t; break; }
            if (part == null) throw new System.Exception(fbxPath + " has no child " + childName);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = part.GetComponent<MeshRenderer>().sharedMaterials;
            // the pack's FBX carry their own placeholder materials ("pal", "mat1"); the project's materials are what they were authored for (palette for props, ground for the platform, force field for the rim, as in the pack's own levels)
            var theme = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (theme != null) { var mats = new Material[mr.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = theme; mr.sharedMaterials = mats; }
            return go;
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
            string[] keys = { null, "Variant_Repair_", "Variant_Shield_", "Variant_Speed_", "Variant_Damage_", "Variant_Weapon_MACHINE_GUN", "Variant_Weapon_SHOTGUN", "Variant_Weapon_ROCKET", "Variant_Weapon_GIGAVOLT", "Variant_Weapon_GRENADE" };
            Transform rocketVariant = null;
            foreach (Transform child in root.transform) if (child.name.StartsWith("Variant_Weapon_ROCKET")) rocketVariant = child;
            AddIconVariant(root.transform, rocketVariant, "Variant_Weapon_GIGAVOLT", "ye_lightningbeam");
            AddIconVariant(root.transform, rocketVariant, "Variant_Weapon_GRENADE", "grenade");
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

        /// <summary>A new item kind: the rocket variant's base, label and bobbing rig, with its icon swapped for a model from the weapon pack.</summary>
        static void AddIconVariant(Transform itemRoot, Transform template, string name, string iconMesh)
        {
            var v = Object.Instantiate(template.gameObject, itemRoot);
            v.name = name;
            Transform vis = v.transform.Find("Visual");
            for (int i = vis.childCount - 1; i >= 0; i--) Object.DestroyImmediate(vis.GetChild(i).gameObject);
            foreach (Component c in vis.GetComponents<Component>()) if (c is MeshRenderer || c is MeshFilter) Object.DestroyImmediate(c);
            GameObject icon = Visual(ModelDir + "Weapons/powerup_icons.fbx", iconMesh, "Icon", vis, Vector3.zero, "Assets/Materials/Palettes/mpe_PowerupIcons.mat");
            icon.transform.localPosition = Vector3.zero;
            icon.transform.localScale = Vector3.one * 2.2f;
        }

        /// <summary>One of the weapon pack's shot visuals (mesh, particles) with its own scripts removed: SpikeShell moves it.</summary>
        static GameObject BuildPackShell(string shotPrefabName)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Shots/" + shotPrefabName + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Shell_" + shotPrefabName;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/Shell_" + shotPrefabName + ".prefab");
            Object.DestroyImmediate(go);
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
        static GameObject BuildProtoTankPrefab(Material shell, GameObject shellPrefab, GameObject[] shellOverrides)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/Prefabs/Tank_Match.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "SpikeTank";
            Transform bar = root.transform.Find("HealthBar");
            if (bar != null) Object.DestroyImmediate(bar.gameObject);

            // the prototype's boxes (hull, tracks, turret, barrel) give way to the weapon pack's tank; only the team ring stays
            Transform vis = root.transform.Find("Visual");
            foreach (string part in new[] { "Hull", "TrackL", "TrackR", "TurretPivot" }) Object.DestroyImmediate(vis.Find(part).gameObject);
            vis.localScale = Vector3.one * TankScale;
            Transform ringT = vis.Find("TeamRing");
            ringT.localScale = new Vector3(4.6f / TankScale, 4.6f / TankScale, 1f); ringT.localPosition = new Vector3(0f, 0.05f / TankScale, 0f);
            var pack = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Game/PlayerTank.prefab"));
            PrefabUtility.UnpackPrefabInstance(pack, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Transform hullGroup = pack.transform.Find("TankParent");
            hullGroup.SetParent(vis, false);
            Transform packTurret = hullGroup.Find("visual_turret");
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/weapon_primary_main.prefab"));
            PrefabUtility.UnpackPrefabInstance(weapon, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(weapon.transform.Find("Exits").gameObject);
            weapon.transform.SetParent(packTurret.Find("PrimaryWeapons"), false);
            weapon.transform.localPosition = Vector3.zero; weapon.transform.localRotation = Quaternion.identity;
            Object.DestroyImmediate(pack);                                         // teleport / damage effects and the other weapons are not used
            foreach (Animator an in root.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);   // scripts of the pack that this project does not have
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name != "TankUnit") Object.DestroyImmediate(mb);
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            root.AddComponent<NetworkIdentity>();
            var tank = root.AddComponent<SpikeTank>();
            tank.visual = vis;
            tank.turret = packTurret;

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
            tank.shellOverrides = shellOverrides;

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
