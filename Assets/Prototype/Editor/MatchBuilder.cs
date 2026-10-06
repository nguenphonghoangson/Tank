using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TankGame.Prototype.Editor
{
    /// <summary>
    /// Builds the objective-match prototype from code: the Crossfire map (a prefab plus a MapDef asset), the match tank,
    /// the cannon WeaponDef, pickup/dash FX and the Match_Prototype scene. Placeholder geometry only.
    /// To add a map: build another prefab with a MapLayout and a MapDef; no gameplay code changes.
    /// </summary>
    public static class MatchBuilder
    {
        const string Root = "Assets/Prototype";
        const string ScenePath = "Assets/Scenes/Match_Prototype.unity";

        [MenuItem("Tank/Prototype/Build Match Scene")]
        public static void BuildMatch()
        {
            // projectile, combat FX and base materials come from the first prototype builder
            if (!File.Exists(Root + "/Prefabs/Fx_Explosion.prefab") || !File.Exists(Root + "/Materials/FxAdditive.mat")) PrototypeBuilder.BuildArena();

            PrototypeBuilder.EnsureFolder(Root + "/Data");
            Texture2D soft = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/SoftCircle.png");
            Texture2D ringTex = MakeRing();
            Texture2D discTex = MakeDisc();
            Texture2D white = MakeWhite();

            Material zoneFill = PrototypeBuilder.Particle("ZoneFill", discTex, false);
            Material zoneRing = PrototypeBuilder.Particle("ZoneRing", ringTex, false);
            Material zoneBeam = PrototypeBuilder.Particle("ZoneBeam", white, false);
            Material wall = Load<Material>("Materials/Wall.mat");
            Material block = Load<Material>("Materials/Block.mat");
            Material fxAdd = Load<Material>("Materials/FxAdditive.mat");
            Material fxSmoke = Load<Material>("Materials/FxSmoke.mat");
            Material reticleMat = Load<Material>("Materials/Reticle.mat");
            Material barBg = Load<Material>("Materials/BarBackground.mat");
            Material barFill = PrototypeBuilder.Unlit("BarTeam", Color.white);
            Material hullMat = PrototypeBuilder.Lit("MatchHull", Color.white);
            Material turretMat = PrototypeBuilder.Lit("MatchTurret", Color.white);
            Material barrelMat = Load<Material>("Materials/Barrel.mat");
            Material trackMat = Load<Material>("Materials/Track.mat");

            WeaponDef cannon = MakeWeapon("Cannon", w =>
            {
                w.damage = 25; w.fireInterval = 0.33f; w.projectileSpeed = 55f; w.range = 70f; w.recoilSpeed = 1.6f;
                w.magazineSize = 10; w.reloadSeconds = 1.6f; w.splashRadius = 2.2f; w.splashDamageFactor = 0.4f;
                w.pellets = 1; w.spreadDegrees = 0f; w.limitedAmmo = false;
                w.projectileScale = Vector3.one; w.projectileColor = new Color(1f, 0.92f, 0.5f);
            });
            WeaponDef machineGun = MakeWeapon("Machine Gun", w =>
            {
                w.damage = 8; w.fireInterval = 0.085f; w.projectileSpeed = 70f; w.range = 48f; w.recoilSpeed = 0.35f;
                w.magazineSize = 45; w.reloadSeconds = 0f; w.splashRadius = 0f; w.pellets = 1; w.spreadDegrees = 3.5f; w.limitedAmmo = true;
                w.projectileScale = new Vector3(0.5f, 0.5f, 0.8f); w.projectileColor = new Color(1f, 1f, 0.75f);
            });
            WeaponDef shotgun = MakeWeapon("Shotgun", w =>
            {
                w.damage = 9; w.fireInterval = 0.75f; w.projectileSpeed = 44f; w.range = 26f; w.recoilSpeed = 3.2f;
                w.magazineSize = 7; w.reloadSeconds = 0f; w.splashRadius = 0f; w.pellets = 7; w.spreadDegrees = 18f; w.limitedAmmo = true;
                w.projectileScale = new Vector3(0.6f, 0.6f, 0.5f); w.projectileColor = new Color(1f, 0.6f, 0.2f);
            });
            WeaponDef rocket = MakeWeapon("Rocket", w =>
            {
                w.damage = 55; w.fireInterval = 1.0f; w.projectileSpeed = 32f; w.range = 70f; w.recoilSpeed = 2.6f;
                w.magazineSize = 4; w.reloadSeconds = 0f; w.splashRadius = 5.5f; w.splashDamageFactor = 0.6f; w.pellets = 1; w.spreadDegrees = 0f; w.limitedAmmo = true;
                w.projectileScale = new Vector3(1.9f, 1.9f, 2.4f); w.projectileColor = new Color(1f, 0.35f, 0.15f);
            });
            Projectile projectile = Load<GameObject>("Prefabs/Projectile.prefab").GetComponent<Projectile>();
            PooledFx muzzle = Load<GameObject>("Prefabs/Fx_MuzzleFlash.prefab").GetComponent<PooledFx>();
            PooledFx impact = Load<GameObject>("Prefabs/Fx_Impact.prefab").GetComponent<PooledFx>();
            PooledFx hit = Load<GameObject>("Prefabs/Fx_Hit.prefab").GetComponent<PooledFx>();
            PooledFx explosion = Load<GameObject>("Prefabs/Fx_Explosion.prefab").GetComponent<PooledFx>();
            PooledFx pickupFx = BuildPickupFx(fxAdd);
            PooledFx dashFx = BuildDashFx(fxAdd, fxSmoke);

            TankUnit tank = BuildMatchTank(hullMat, turretMat, barrelMat, trackMat, zoneRing, barBg, barFill);
            GameObject mapPrefab = BuildMap(wall, block, zoneFill, zoneRing, zoneBeam, machineGun, shotgun, rocket);
            MapDef mapDef = MakeMapDef(mapPrefab);

            // ---------------------------------------------------------------- scene
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 160f;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CameraRig>();
            rig.aimLookahead = 0.1f;      // the view leans toward the aim point, but only a little: a strong lean feels like being pulled
            rig.maxLookahead = 4f;
            camGo.transform.position = new Vector3(0f, 22f, -13f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -22f, 13f).normalized);

            var fxGo = new GameObject("CombatFx");
            var fx = fxGo.AddComponent<CombatFx>();
            fx.projectilePrefab = projectile;
            fx.muzzlePrefab = muzzle;
            fx.impactPrefab = impact;
            fx.hitPrefab = hit;
            fx.explosionPrefab = explosion;
            fx.pickupPrefab = pickupFx;
            fx.dashPrefab = dashFx;
            fx.cameraRig = rig;
            var audio = fxGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            fx.audioSource = audio;

            var ret = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(ret.GetComponent<Collider>());
            ret.name = "AimReticle";
            ret.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ret.transform.localScale = new Vector3(1.4f, 1.4f, 1f);
            ret.GetComponent<MeshRenderer>().sharedMaterial = reticleMat;
            ret.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var inputGo = new GameObject("LocalPlayerInput");
            var input = inputGo.AddComponent<PlayerTankInput>();
            input.cam = cam;
            input.rig = rig;
            input.reticle = ret.transform;
            input.enabled = false;

            var matchGo = new GameObject("Match");
            var match = matchGo.AddComponent<MatchManager>();
            match.map = mapDef;
            match.tankPrefab = tank;
            match.weapon = cannon;
            match.fx = fx;
            match.cameraRig = rig;
            match.localInput = input;
            var hud = matchGo.AddComponent<MatchHud>();
            hud.match = match;
            hud.cam = cam;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Match prototype built: " + ScenePath);
        }

        // ------------------------------------------------------------------ data assets

        static WeaponDef MakeWeapon(string name, System.Action<WeaponDef> setup)
        {
            string path = Root + "/Data/Weapon_" + name + ".asset";
            var w = AssetDatabase.LoadAssetAtPath<WeaponDef>(path);
            if (w == null) { w = ScriptableObject.CreateInstance<WeaponDef>(); AssetDatabase.CreateAsset(w, path); }
            w.displayName = name;
            setup(w);
            EditorUtility.SetDirty(w);
            return w;
        }

        static MapDef MakeMapDef(GameObject prefab)
        {
            const string path = Root + "/Data/Map_Crossfire.asset";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);     // recreate: keeps the script reference valid
            var d = ScriptableObject.CreateInstance<MapDef>();
            AssetDatabase.CreateAsset(d, path);
            d.displayName = "Crossfire";
            d.prefab = prefab;
            EditorUtility.SetDirty(d);
            return d;
        }

        // ------------------------------------------------------------------ tank

        static TankUnit BuildMatchTank(Material hull, Material turret, Material barrel, Material track, Material ringMat, Material barBg, Material barFill)
        {
            var root = new GameObject("Tank_Match");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 8f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.6f, 0f);
            col.size = new Vector3(2.1f, 1.2f, 3.3f);

            Transform vis = new GameObject("Visual").transform;
            vis.SetParent(root.transform, false);
            Transform teamRing = PrototypeBuilder.Quad("TeamRing", vis, new Vector3(0f, 0.05f, 0f), new Vector3(4.6f, 4.6f, 1f), ringMat);
            teamRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            PrototypeBuilder.Block("Hull", vis, new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 0.7f, 3f), hull, false);
            PrototypeBuilder.Block("TrackL", vis, new Vector3(-1.1f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 3.3f), track, false);
            PrototypeBuilder.Block("TrackR", vis, new Vector3(1.1f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 3.3f), track, false);
            Transform pivot = new GameObject("TurretPivot").transform;
            pivot.SetParent(vis, false);
            pivot.localPosition = new Vector3(0f, 1.1f, 0f);
            PrototypeBuilder.Block("Turret", pivot, new Vector3(0f, 0.15f, 0f), new Vector3(1.3f, 0.55f, 1.5f), turret, false);
            Transform barrelT = PrototypeBuilder.Block("Barrel", pivot, new Vector3(0f, 0.15f, 1.5f), new Vector3(0.26f, 0.26f, 2f), barrel, false);
            Transform muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(pivot, false);
            muzzle.localPosition = new Vector3(0f, 0.15f, 2.55f);

            var unit = root.AddComponent<TankUnit>();
            unit.turretPivot = pivot;
            unit.muzzle = muzzle;
            unit.bodyCollider = col;

            var dash = root.AddComponent<DashSkill>();
            dash.displayName = "Dash";
            dash.cooldown = 6f;
            dash.speed = 36f;
            dash.duration = 0.22f;

            var fb = root.AddComponent<TankFeedback>();
            fb.unit = unit;
            fb.visualRoot = vis;
            fb.barrel = barrelT;
            fb.recoilDistance = 0.4f;

            var hb = new GameObject("HealthBar").transform;
            hb.SetParent(root.transform, false);
            hb.localPosition = new Vector3(0f, 3.4f, 0f);
            var barRoot = new GameObject("Bar").transform;
            barRoot.SetParent(hb, false);
            PrototypeBuilder.Quad("Back", barRoot, Vector3.zero, new Vector3(2.5f, 0.3f, 1f), barBg);
            Transform fill = PrototypeBuilder.Quad("Fill", barRoot, new Vector3(0f, 0f, -0.02f), new Vector3(2.4f, 0.2f, 1f), barFill);
            var bar = hb.gameObject.AddComponent<HealthBar>();
            bar.unit = unit;
            bar.barRoot = barRoot.gameObject;
            bar.fill = fill;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Tank_Match.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<TankUnit>();
        }

        // ------------------------------------------------------------------ map

        static GameObject BuildMap(Material wall, Material block, Material zoneFill, Material zoneRing, Material zoneBeam,
            WeaponDef machineGun, WeaponDef shotgun, WeaponDef rocket)
        {
            const float Half = 60f;
            Texture2D grid = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Grid.png");
            Material ground = PrototypeBuilder.Lit("GroundLarge", Color.white, grid, Half);   // 2 m cells over the whole floor

            var root = new GameObject("Map_Crossfire");
            var layout = root.AddComponent<MapLayout>();
            layout.mapName = "Crossfire";
            layout.arenaHalfSize = Half - 2f;

            Transform geo = new GameObject("Geometry").transform;
            geo.SetParent(root.transform, false);
            PrototypeBuilder.Block("Ground", geo, new Vector3(0f, -0.5f, 0f), new Vector3(Half * 2f, 1f, Half * 2f), ground, true);
            PrototypeBuilder.Block("Wall_N", geo, new Vector3(0f, 1.5f, Half + 0.5f), new Vector3(Half * 2f + 2f, 3f, 1f), wall, true);
            PrototypeBuilder.Block("Wall_S", geo, new Vector3(0f, 1.5f, -Half - 0.5f), new Vector3(Half * 2f + 2f, 3f, 1f), wall, true);
            PrototypeBuilder.Block("Wall_E", geo, new Vector3(Half + 0.5f, 1.5f, 0f), new Vector3(1f, 3f, Half * 2f + 2f), wall, true);
            PrototypeBuilder.Block("Wall_W", geo, new Vector3(-Half - 0.5f, 1.5f, 0f), new Vector3(1f, 3f, Half * 2f + 2f), wall, true);

            // five points, far apart: centre, west, east, south, north
            Vector3[] pointPos = { new Vector3(-42f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(42f, 0f, 0f), new Vector3(0f, 0f, -40f), new Vector3(0f, 0f, 40f) };
            string[] pointNames = { "A", "B", "C", "D", "E" };
            float[] pointWeight = { 1f, 1.5f, 1f, 1f, 1f };

            // cover: a pillar in the centre, partial cover around every point, low cover on the lanes
            var cover = new System.Collections.Generic.List<Transform>();
            int n = 0;
            System.Action<Vector3, Vector3> add = (p, size) => cover.Add(PrototypeBuilder.Block("Cover_" + (n++), geo, new Vector3(p.x, size.y * 0.5f, p.z), size, block, true));
            add(new Vector3(0f, 0f, 0f), new Vector3(2.5f, 2f, 2.5f));
            foreach (Vector3 p in pointPos)
            {
                add(p + new Vector3(-12f, 0f, 7f), new Vector3(5f, 2f, 2f));
                add(p + new Vector3(12f, 0f, -7f), new Vector3(5f, 2f, 2f));
                add(p + new Vector3(0f, 0f, p.z == 0f ? 14f : (p.z < 0f ? 13f : -13f)), new Vector3(2f, 2f, 5f));
            }
            Vector3[] lane = { new Vector3(20f, 0f, 20f), new Vector3(-20f, 0f, 20f), new Vector3(20f, 0f, -20f), new Vector3(-20f, 0f, -20f) };
            foreach (Vector3 p in lane) add(p, new Vector3(4f, 1.5f, 4f));
            Vector3[] corner = { new Vector3(32f, 0f, 32f), new Vector3(-32f, 0f, 32f), new Vector3(32f, 0f, -32f), new Vector3(-32f, 0f, -32f), new Vector3(46f, 0f, 20f), new Vector3(-46f, 0f, -20f), new Vector3(46f, 0f, -20f), new Vector3(-46f, 0f, 20f) };
            foreach (Vector3 p in corner) add(p, new Vector3(6f, 2f, 6f));
            layout.minimapBlocks = cover.ToArray();

            Transform pts = new GameObject("ControlPoints").transform;
            pts.SetParent(root.transform, false);
            layout.controlPoints = new ControlPoint[pointPos.Length];
            for (int i = 0; i < pointPos.Length; i++)
            {
                layout.controlPoints[i] = BuildPoint(pointNames[i], pointPos[i], pts, zoneFill, zoneRing, zoneBeam);
                layout.controlPoints[i].weight = pointWeight[i];
            }

            // item slots: every slot rolls a random item (weighted) each time it appears, so no two matches look alike
            Transform pk = new GameObject("Pickups").transform;
            pk.SetParent(root.transform, false);
            Color green = new Color(0.35f, 1f, 0.5f), cyan = new Color(0.3f, 0.85f, 1f), yellow = new Color(1f, 0.9f, 0.25f), red = new Color(1f, 0.3f, 0.25f);
            Color orange = new Color(1f, 0.65f, 0.15f), magenta = new Color(0.95f, 0.35f, 0.9f), violet = new Color(0.7f, 0.5f, 1f);
            var specs = new[]
            {
                new VariantSpec(PickupKind.Repair, "REPAIR", green, null, 3f, 25f),
                new VariantSpec(PickupKind.Shield, "SHIELD", cyan, null, 2f, 30f),
                new VariantSpec(PickupKind.Speed, "SPEED", yellow, null, 2f, 30f),
                new VariantSpec(PickupKind.Damage, "DAMAGE x1.5", red, null, 1.5f, 40f),
                new VariantSpec(PickupKind.Weapon, "MACHINE GUN", orange, machineGun, 1.5f, 35f),
                new VariantSpec(PickupKind.Weapon, "SHOTGUN", magenta, shotgun, 1.5f, 35f),
                new VariantSpec(PickupKind.Weapon, "ROCKET", violet, rocket, 1f, 45f),
            };
            Vector3[] slotPos =
            {
                new Vector3(-24f, 0f, 10f), new Vector3(24f, 0f, -10f), new Vector3(24f, 0f, 10f), new Vector3(-24f, 0f, -10f),
                new Vector3(-8f, 0f, -24f), new Vector3(8f, 0f, 24f), new Vector3(0f, 0f, -24f), new Vector3(0f, 0f, 24f),
                new Vector3(-34f, 0f, -24f), new Vector3(34f, 0f, 24f), new Vector3(34f, 0f, -24f), new Vector3(-34f, 0f, 24f),
                new Vector3(0f, 0f, -13f), new Vector3(0f, 0f, 13f),
            };
            layout.pickups = new Pickup[slotPos.Length];
            for (int i = 0; i < slotPos.Length; i++) layout.pickups[i] = BuildSlot("Slot_" + i, slotPos[i], pk, specs);

            // spawn groups on the edges: 0 south, 1 north, 2 east, 3 west, 4 south-west corner
            // (2 teams use 0-1, 3 teams 0-2, solo 5 uses all)
            Transform sp = new GameObject("Spawns").transform;
            sp.SetParent(root.transform, false);
            layout.spawnGroups = new[]
            {
                Group("South", sp, new Vector3(-6f, 0f, -55f), new Vector3(0f, 0f, -55f), new Vector3(6f, 0f, -55f)),
                Group("North", sp, new Vector3(6f, 0f, 55f), new Vector3(0f, 0f, 55f), new Vector3(-6f, 0f, 55f)),
                Group("East", sp, new Vector3(55f, 0f, -6f), new Vector3(55f, 0f, 0f), new Vector3(55f, 0f, 6f)),
                Group("West", sp, new Vector3(-55f, 0f, 6f), new Vector3(-55f, 0f, 0f), new Vector3(-55f, 0f, -6f)),
                Group("SouthWest", sp, new Vector3(-50f, 0f, -50f), new Vector3(-46f, 0f, -53f), new Vector3(-53f, 0f, -46f)),
            };

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Map_Crossfire.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static SpawnGroup Group(string name, Transform parent, params Vector3[] positions)
        {
            var g = new GameObject("Spawn_" + name).transform;
            g.SetParent(parent, false);
            var pts = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var t = new GameObject("P" + i).transform;
                t.SetParent(g, false);
                t.position = positions[i];
                pts[i] = t;
            }
            return new SpawnGroup { points = pts };
        }

        static ControlPoint BuildPoint(string label, Vector3 pos, Transform parent, Material fillMat, Material ringMat, Material beamMat)
        {
            const float Radius = 8f;
            var go = new GameObject("Point_" + label);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var cp = go.AddComponent<ControlPoint>();
            cp.label = label;
            cp.radius = Radius;
            cp.weight = 1f;

            Transform ring = PrototypeBuilder.Quad("Ring", go.transform, new Vector3(0f, 0.04f, 0f), new Vector3(Radius * 2f, Radius * 2f, 1f), ringMat);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Transform fill = PrototypeBuilder.Quad("Fill", go.transform, new Vector3(0f, 0.05f, 0f), new Vector3(Radius * 2f, Radius * 2f, 1f), fillMat);
            fill.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.name = "Beam";
            beam.transform.SetParent(go.transform, false);
            beam.transform.localPosition = new Vector3(0f, 6f, 0f);
            beam.transform.localScale = new Vector3(1.3f, 6f, 1.3f);
            var br = beam.GetComponent<MeshRenderer>();
            br.sharedMaterial = beamMat;
            br.shadowCastingMode = ShadowCastingMode.Off;
            br.receiveShadows = false;

            // flag: a pole and a cloth that rises as the hold grows and drops as it is lost
            Material poleMat = PrototypeBuilder.Lit("FlagPole", new Color(0.18f, 0.19f, 0.22f));
            Material clothMat = PrototypeBuilder.Unlit("FlagCloth", Color.white);
            PrototypeBuilder.Block("Pole", go.transform, new Vector3(0f, 3.8f, 0f), new Vector3(0.22f, 7.6f, 0.22f), poleMat, false);
            Transform cloth = PrototypeBuilder.Block("Flag", go.transform, new Vector3(1.5f, 1.2f, 0f), new Vector3(2.8f, 1.6f, 0.12f), clothMat, false);
            cloth.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 13.5f, 0f);
            labelGo.transform.rotation = Quaternion.Euler(59.4f, 0f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = 64;
            tm.characterSize = 0.3f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 1f, 1f, 0.95f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            cp.fill = fill.GetComponent<Renderer>();
            cp.ring = ring.GetComponent<Renderer>();
            cp.beam = br;
            cp.flag = cloth;
            cp.flagRenderer = cloth.GetComponent<Renderer>();
            return cp;
        }

        struct VariantSpec
        {
            public PickupKind kind; public string label; public Color color; public WeaponDef weapon; public float weight, respawn;
            public VariantSpec(PickupKind kind, string label, Color color, WeaponDef weapon, float weight, float respawn)
            { this.kind = kind; this.label = label; this.color = color; this.weapon = weapon; this.weight = weight; this.respawn = respawn; }
        }

        static Pickup BuildSlot(string name, Vector3 pos, Transform parent, VariantSpec[] specs)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.8f;
            trigger.center = new Vector3(0f, 0.6f, 0f);

            var variants = new PickupVariant[specs.Length];
            for (int i = 0; i < specs.Length; i++) variants[i] = BuildVariant(go.transform, specs[i]);
            var p = go.AddComponent<Pickup>();
            p.variants = variants;
            return p;
        }

        static PickupVariant BuildVariant(Transform slot, VariantSpec spec)
        {
            Texture2D soft = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/SoftCircle.png");
            string key = spec.kind + "_" + spec.label.Replace(' ', '_').Replace('.', '_');
            Material mat = PrototypeBuilder.Unlit("Item_" + key, spec.color);
            Material baseMat = PrototypeBuilder.Particle("ItemBase_" + key, soft, false);
            baseMat.SetColor("_BaseColor", new Color(spec.color.r, spec.color.g, spec.color.b, 0.6f));

            var root = new GameObject("Variant_" + key);
            root.transform.SetParent(slot, false);

            Transform b = PrototypeBuilder.Quad("Base", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(3.6f, 3.6f, 1f), baseMat);
            b.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Transform vis = new GameObject("Visual").transform;
            vis.SetParent(root.transform, false);
            vis.localPosition = new Vector3(0f, 1.2f, 0f);
            switch (spec.kind)
            {
                case PickupKind.Repair:
                    PrototypeBuilder.Block("H", vis, Vector3.zero, new Vector3(1.1f, 0.34f, 0.34f), mat, false);
                    PrototypeBuilder.Block("V", vis, Vector3.zero, new Vector3(0.34f, 1.1f, 0.34f), mat, false);
                    PrototypeBuilder.Block("D", vis, Vector3.zero, new Vector3(0.34f, 0.34f, 1.1f), mat, false);
                    break;
                case PickupKind.Shield:
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(s.GetComponent<Collider>());
                    s.name = "Orb"; s.transform.SetParent(vis, false); s.transform.localScale = Vector3.one * 1.1f;
                    s.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    break;
                }
                case PickupKind.Speed:
                {
                    Transform a = PrototypeBuilder.Block("ChevronA", vis, new Vector3(-0.25f, 0f, 0.3f), new Vector3(0.28f, 0.28f, 1.0f), mat, false);
                    a.localRotation = Quaternion.Euler(0f, 40f, 0f);
                    Transform c = PrototypeBuilder.Block("ChevronB", vis, new Vector3(0.25f, 0f, 0.3f), new Vector3(0.28f, 0.28f, 1.0f), mat, false);
                    c.localRotation = Quaternion.Euler(0f, -40f, 0f);
                    break;
                }
                case PickupKind.Damage:
                {
                    Transform a = PrototypeBuilder.Block("CrossA", vis, Vector3.zero, new Vector3(1.3f, 0.3f, 0.3f), mat, false);
                    a.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    Transform c = PrototypeBuilder.Block("CrossB", vis, Vector3.zero, new Vector3(1.3f, 0.3f, 0.3f), mat, false);
                    c.localRotation = Quaternion.Euler(0f, 0f, -45f);
                    break;
                }
                default:
                    PrototypeBuilder.Block("Barrel", vis, new Vector3(0f, 0f, 0.2f), new Vector3(0.3f, 0.3f, 1.3f), mat, false);
                    PrototypeBuilder.Block("Body", vis, new Vector3(0f, -0.1f, -0.4f), new Vector3(0.7f, 0.5f, 0.6f), mat, false);
                    break;
            }

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            labelGo.transform.rotation = Quaternion.Euler(59.4f, 0f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = spec.label;
            tm.fontSize = 48;
            tm.characterSize = 0.18f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = spec.color;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            root.SetActive(false);
            return new PickupVariant
            {
                kind = spec.kind, label = spec.label, color = spec.color, weapon = spec.weapon,
                weight = spec.weight, respawnSeconds = spec.respawn, root = root, visual = vis,
            };
        }

        // ------------------------------------------------------------------ fx

        static PooledFx BuildPickupFx(Material add)
        {
            var root = new GameObject("Fx_Pickup");
            var green = new Color(0.45f, 1f, 0.55f, 1f);
            var white = new Color(0.9f, 1f, 0.9f, 1f);
            PrototypeBuilder.Burst(root, "Flash", add, 1, 0.15f, 0.15f, 0f, 0f, 3.6f, 3.6f, green, white, 0f, ParticleSystemShapeType.Sphere, 0.01f, false);
            PrototypeBuilder.Burst(root, "Sparkles", add, 18, 0.5f, 0.9f, 2f, 6f, 0.2f, 0.4f, green, white, -0.4f, ParticleSystemShapeType.Sphere, 0.6f, false);
            var fx = root.AddComponent<PooledFx>();
            fx.duration = 1.1f;
            fx.flashLight = PrototypeBuilder.AddLight(root, green, 8f, 4f);
            fx.flashDuration = 0.2f;
            return PrototypeBuilder.Save(root, "Fx_Pickup");
        }

        static PooledFx BuildDashFx(Material add, Material smoke)
        {
            var root = new GameObject("Fx_Dash");
            var dust = new Color(0.7f, 0.68f, 0.62f, 0.55f);
            var white = new Color(1f, 0.95f, 0.8f, 1f);
            // emitted along the opposite of the dash direction (the prefab is rotated that way when spawned)
            PrototypeBuilder.Burst(root, "Dust", smoke, 9, 0.4f, 0.7f, 2f, 7f, 0.9f, 1.8f, dust, dust, 0f, ParticleSystemShapeType.Cone, 22f, false);
            PrototypeBuilder.Burst(root, "Sparks", add, 12, 0.15f, 0.3f, 8f, 18f, 0.1f, 0.18f, white, white, 0f, ParticleSystemShapeType.Cone, 14f, true);
            var fx = root.AddComponent<PooledFx>();
            fx.duration = 0.9f;
            return PrototypeBuilder.Save(root, "Fx_Dash");
        }

        // ------------------------------------------------------------------ textures

        static Texture2D MakeRing()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.94f) / 0.045f) * (r < 1f ? 1f : 0f);   // thin ring
                    a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.90f) / 0.12f) * 0.25f);          // faint inner glow
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            return Save(t, "Ring");
        }

        static Texture2D MakeDisc()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((0.97f - r) / 0.03f)));
                }
            return Save(t, "Disc");
        }

        static Texture2D MakeWhite()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) t.SetPixel(x, y, Color.white);
            return Save(t, "White");
        }

        static Texture2D Save(Texture2D tex, string name)
        {
            string path = Root + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static T Load<T>(string relativePath) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(Root + "/" + relativePath);
        }
    }
}
