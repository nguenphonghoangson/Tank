using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TankGame.Prototype.Editor
{
    /// <summary>
    /// Rebuilds every prototype asset (materials, FX and tank prefabs) and the Prototype_Arena scene from code.
    /// Placeholder geometry only. Re-running overwrites; nothing here touches Bootstrap, URP or Quality settings.
    /// </summary>
    public static class PrototypeBuilder
    {
        const string Root = "Assets/Prototype";
        const string ScenePath = "Assets/Scenes/Prototype_Arena.unity";

        [MenuItem("Tank/Prototype/Build Arena Scene")]
        public static void BuildArena()
        {
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Textures");

            Texture2D soft = MakeSoftCircle();
            Texture2D grid = MakeGrid();

            Material ground = Lit("Ground", new Color(0.13f, 0.15f, 0.19f), grid, 30f);
            Material wall = Lit("Wall", new Color(0.30f, 0.33f, 0.40f));
            Material block = Lit("Block", new Color(0.42f, 0.38f, 0.30f));
            Material pHull = Lit("PlayerHull", new Color(0.18f, 0.55f, 0.95f));
            Material pTurret = Lit("PlayerTurret", new Color(0.35f, 0.72f, 1f));
            Material eHull = Lit("EnemyHull", new Color(0.88f, 0.28f, 0.2f));
            Material eTurret = Lit("EnemyTurret", new Color(1f, 0.5f, 0.35f));
            Material barrel = Lit("Barrel", new Color(0.82f, 0.84f, 0.88f));
            Material track = Lit("Track", new Color(0.09f, 0.09f, 0.1f));
            Material debris = Lit("Debris", new Color(0.12f, 0.12f, 0.13f));
            Material barBg = Unlit("BarBackground", new Color(0.05f, 0.05f, 0.06f));
            Material barPlayer = Unlit("BarPlayer", new Color(0.3f, 0.9f, 0.4f));
            Material barEnemy = Unlit("BarEnemy", new Color(1f, 0.3f, 0.25f));
            Material shell = Unlit("Projectile", new Color(1f, 0.92f, 0.5f));
            Material fxAdd = Particle("FxAdditive", soft, true);
            Material fxSmoke = Particle("FxSmoke", soft, false);
            Material reticleMat = Particle("Reticle", soft, false);

            Projectile projectile = BuildProjectile(shell, fxAdd);
            PooledFx muzzle = BuildMuzzle(fxAdd);
            PooledFx impact = BuildImpact("Fx_Impact", fxAdd, fxSmoke, false);
            PooledFx hit = BuildImpact("Fx_Hit", fxAdd, fxSmoke, true);
            PooledFx explosion = BuildExplosion(fxAdd, fxSmoke, debris);

            GameObject playerPrefab = BuildTank("Tank_Player", pHull, pTurret, barrel, track, barBg, barPlayer, 0);
            GameObject enemyPrefab = BuildTank("Tank_Enemy", eHull, eTurret, barrel, track, barBg, barEnemy, 1);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;

            // arena
            Transform arena = new GameObject("Arena").transform;
            Block("Ground", arena, new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), ground, true);
            Block("Wall_N", arena, new Vector3(0f, 1.5f, 30.5f), new Vector3(62f, 3f, 1f), wall, true);
            Block("Wall_S", arena, new Vector3(0f, 1.5f, -30.5f), new Vector3(62f, 3f, 1f), wall, true);
            Block("Wall_E", arena, new Vector3(30.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), wall, true);
            Block("Wall_W", arena, new Vector3(-30.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), wall, true);
            Vector4[] blocks =
            {
                new Vector4(-12f, 1f, -8f, 0), new Vector4(12f, 1f, -8f, 0), new Vector4(-14f, 1f, 8f, 0), new Vector4(14f, 1f, 8f, 0),
                new Vector4(-6f, 0.75f, 16f, 0), new Vector4(7f, 0.75f, -15f, 0), new Vector4(-21f, 1f, -2f, 0), new Vector4(21f, 1f, -2f, 0),
            };
            Vector3[] sizes =
            {
                new Vector3(4, 2, 4), new Vector3(4, 2, 4), new Vector3(6, 2, 3), new Vector3(3, 2, 6),
                new Vector3(3, 1.5f, 3), new Vector3(3, 1.5f, 3), new Vector3(2, 2, 6), new Vector3(2, 2, 6),
            };
            for (int i = 0; i < blocks.Length; i++)
                Block("Block_" + i, arena, new Vector3(blocks[i].x, blocks[i].y, blocks[i].z), sizes[i], block, true);

            // light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            // camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 150f;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CameraRig>();
            camGo.transform.position = new Vector3(0f, 22f, -33f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -22f, 13f).normalized);

            // combat fx hub
            var fxGo = new GameObject("CombatFx");
            var fx = fxGo.AddComponent<CombatFx>();
            fx.projectilePrefab = projectile;
            fx.muzzlePrefab = muzzle;
            fx.impactPrefab = impact;
            fx.hitPrefab = hit;
            fx.explosionPrefab = explosion;
            fx.cameraRig = rig;
            var audio = fxGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            fx.audioSource = audio;

            // spawns
            Transform spawns = new GameObject("Spawns").transform;
            Transform playerSpawn = Spawn("PlayerSpawn", spawns, new Vector3(0f, 0f, -20f), 0f);
            Vector3[] ep = { new Vector3(0f, 0f, 16f), new Vector3(-18f, 0f, 14f), new Vector3(18f, 0f, 14f), new Vector3(0f, 0f, 24f) };
            Transform[] enemySpawns = new Transform[ep.Length];
            for (int i = 0; i < ep.Length; i++) enemySpawns[i] = Spawn("EnemySpawn_" + i, spawns, ep[i], 180f);

            // tanks
            GameObject playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            playerGo.transform.SetPositionAndRotation(playerSpawn.position, playerSpawn.rotation);
            GameObject enemyGo = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
            enemyGo.transform.SetPositionAndRotation(enemySpawns[0].position, enemySpawns[0].rotation);
            TankUnit player = playerGo.GetComponent<TankUnit>();
            TankUnit enemy = enemyGo.GetComponent<TankUnit>();
            player.fx = fx; playerGo.GetComponent<TankFeedback>().fx = fx;
            enemy.fx = fx; enemyGo.GetComponent<TankFeedback>().fx = fx;

            var brain = enemyGo.AddComponent<EnemyTankBrain>();
            brain.self = enemy;
            brain.target = player;

            rig.target = playerGo.transform;

            // reticle (flat soft ring following the mouse aim point)
            var ret = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(ret.GetComponent<Collider>());
            ret.name = "AimReticle";
            ret.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ret.transform.localScale = new Vector3(1.4f, 1.4f, 1f);
            ret.GetComponent<MeshRenderer>().sharedMaterial = reticleMat;
            ret.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var input = playerGo.AddComponent<PlayerTankInput>();
            input.tank = player;
            input.cam = cam;
            input.rig = rig;
            input.reticle = ret.transform;

            var gameGo = new GameObject("PrototypeGame");
            var game = gameGo.AddComponent<PrototypeGame>();
            game.player = player;
            game.enemy = enemy;
            game.playerSpawn = playerSpawn;
            game.enemySpawns = enemySpawns;
            var hud = gameGo.AddComponent<PrototypeHud>();
            hud.game = game;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Prototype arena built: " + ScenePath);
        }

        // ------------------------------------------------------------------ tank / projectile

        internal static GameObject BuildTank(string name, Material hull, Material turret, Material barrel, Material track, Material barBg, Material barFill, int team)
        {
            var root = new GameObject(name);
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
            Block("Hull", vis, new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 0.7f, 3f), hull, false);
            Block("TrackL", vis, new Vector3(-1.1f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 3.3f), track, false);
            Block("TrackR", vis, new Vector3(1.1f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 3.3f), track, false);
            Transform pivot = new GameObject("TurretPivot").transform;
            pivot.SetParent(vis, false);
            pivot.localPosition = new Vector3(0f, 1.1f, 0f);
            Block("Turret", pivot, new Vector3(0f, 0.15f, 0f), new Vector3(1.3f, 0.55f, 1.5f), turret, false);
            Transform barrelT = Block("Barrel", pivot, new Vector3(0f, 0.15f, 1.5f), new Vector3(0.26f, 0.26f, 2f), barrel, false);
            Transform muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(pivot, false);
            muzzle.localPosition = new Vector3(0f, 0.15f, 2.55f);

            var unit = root.AddComponent<TankUnit>();
            unit.turretPivot = pivot;
            unit.muzzle = muzzle;
            unit.bodyCollider = col;
            unit.team = team;
            unit.isLocal = team == 0;
            if (team == 1)
            {
                unit.fireInterval = 2.2f;
                unit.projectileDamage = 10;
                unit.projectileSpeed = 28f;
            }

            var fb = root.AddComponent<TankFeedback>();
            fb.unit = unit;
            fb.visualRoot = vis;
            fb.barrel = barrelT;
            fb.isPlayer = team == 0;
            fb.recoilDistance = team == 0 ? 0.42f : 0.3f;

            // health bar
            var hb = new GameObject("HealthBar").transform;
            hb.SetParent(root.transform, false);
            hb.localPosition = new Vector3(0f, 3.4f, 0f);
            var barRoot = new GameObject("Bar").transform;
            barRoot.SetParent(hb, false);
            Quad("Back", barRoot, Vector3.zero, new Vector3(2.5f, 0.3f, 1f), barBg);
            Transform fill = Quad("Fill", barRoot, new Vector3(0f, 0f, -0.02f), new Vector3(2.4f, 0.2f, 1f), barFill);
            var bar = hb.gameObject.AddComponent<HealthBar>();
            bar.unit = unit;
            bar.barRoot = barRoot.gameObject;
            bar.fill = fill;

            string path = Root + "/Prefabs/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        internal static Projectile BuildProjectile(Material shell, Material trailMat)
        {
            var root = new GameObject("Projectile");
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = "Shell";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.28f, 0.28f, 1.0f);   // stretched along travel direction for readability
            var mr = body.GetComponent<MeshRenderer>();
            mr.sharedMaterial = shell;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.16f;
            trail.minVertexDistance = 0.1f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 0f));
            trail.colorGradient = Grad(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.4f, 0.1f, 0f));
            trail.sharedMaterial = trailMat;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.generateLightingData = false;

            var p = root.AddComponent<Projectile>();
            p.trail = trail;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Projectile.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Projectile>();
        }

        // ------------------------------------------------------------------ FX

        internal static PooledFx BuildMuzzle(Material add)
        {
            var root = new GameObject("Fx_MuzzleFlash");
            var white = new Color(1f, 0.92f, 0.6f, 1f);
            var orange = new Color(1f, 0.55f, 0.15f, 1f);
            Burst(root, "Core", add, 1, 0.07f, 0.07f, 0f, 0f, 3.4f, 3.4f, white, white, 0f, ParticleSystemShapeType.Sphere, 0.01f, false);
            Burst(root, "Flame", add, 12, 0.08f, 0.16f, 6f, 18f, 0.55f, 1.1f, white, orange, 0f, ParticleSystemShapeType.Cone, 9f, false);
            Burst(root, "Sparks", add, 7, 0.12f, 0.25f, 14f, 26f, 0.08f, 0.14f, white, orange, 0.3f, ParticleSystemShapeType.Cone, 16f, true);
            var fx = root.AddComponent<PooledFx>();
            fx.duration = 0.45f;
            fx.flashLight = AddLight(root, new Color(1f, 0.7f, 0.3f), 10f, 8f);
            fx.flashDuration = 0.07f;
            return Save(root, "Fx_MuzzleFlash");
        }

        internal static PooledFx BuildImpact(string name, Material add, Material smoke, bool onTank)
        {
            var root = new GameObject(name);
            var white = new Color(1f, 0.95f, 0.7f, 1f);
            var orange = onTank ? new Color(1f, 0.4f, 0.1f, 1f) : new Color(1f, 0.65f, 0.25f, 1f);
            Burst(root, "Core", add, 1, 0.08f, 0.08f, 0f, 0f, onTank ? 3.4f : 1.9f, onTank ? 3.4f : 1.9f, white, white, 0f, ParticleSystemShapeType.Sphere, 0.01f, false);
            Burst(root, "Sparks", add, onTank ? 34 : 20, 0.25f, 0.55f, 6f, 18f, 0.16f, 0.28f, white, orange, 1.6f, ParticleSystemShapeType.Cone, 55f, true);
            Burst(root, "Dust", smoke, onTank ? 3 : 5, 0.35f, 0.6f, 1f, 3f, 0.7f, 1.4f, new Color(0.55f, 0.5f, 0.45f, 0.5f), new Color(0.35f, 0.33f, 0.3f, 0.4f), -0.05f, ParticleSystemShapeType.Hemisphere, 0.3f, false);
            var fx = root.AddComponent<PooledFx>();
            fx.duration = 1f;
            fx.flashLight = AddLight(root, new Color(1f, 0.75f, 0.4f), onTank ? 8f : 5f, onTank ? 5f : 3f);
            fx.flashDuration = 0.06f;
            return Save(root, name);
        }

        internal static PooledFx BuildExplosion(Material add, Material smoke, Material debris)
        {
            var root = new GameObject("Fx_Explosion");
            var white = new Color(1f, 0.95f, 0.75f, 1f);
            var orange = new Color(1f, 0.5f, 0.12f, 1f);
            var red = new Color(0.9f, 0.2f, 0.05f, 1f);
            Burst(root, "Flash", add, 1, 0.14f, 0.14f, 0f, 0f, 11f, 11f, white, white, 0f, ParticleSystemShapeType.Sphere, 0.01f, false);
            Burst(root, "Fireball", add, 22, 0.4f, 0.85f, 2f, 9f, 3.2f, 6f, orange, red, 0f, ParticleSystemShapeType.Sphere, 1.0f, false);
            Burst(root, "Embers", add, 48, 0.5f, 1.2f, 10f, 28f, 0.16f, 0.3f, white, orange, 1f, ParticleSystemShapeType.Sphere, 0.5f, true);
            Burst(root, "Smoke", smoke, 12, 1.2f, 1.9f, 0.8f, 3.5f, 3f, 5.5f, new Color(0.2f, 0.2f, 0.2f, 0.7f), new Color(0.35f, 0.33f, 0.3f, 0.6f), -0.15f, ParticleSystemShapeType.Sphere, 0.8f, false);

            ParticleSystem d = Burst(root, "Debris", smoke, 12, 1.0f, 1.6f, 7f, 15f, 0.3f, 0.6f, Color.white, Color.white, 1.6f, ParticleSystemShapeType.Hemisphere, 0.6f, false);
            var r = d.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.sharedMaterial = debris;
            var rot = d.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            var colOver = d.colorOverLifetime;
            colOver.enabled = false;
            var coll = d.collision;
            coll.enabled = true;
            coll.type = ParticleSystemCollisionType.World;
            coll.mode = ParticleSystemCollisionMode.Collision3D;
            coll.bounce = 0.3f;
            coll.dampen = 0.5f;
            coll.lifetimeLoss = 0.05f;
            coll.radiusScale = 0.5f;
            coll.quality = ParticleSystemCollisionQuality.Low;

            var fx = root.AddComponent<PooledFx>();
            fx.duration = 2.2f;
            fx.flashLight = AddLight(root, new Color(1f, 0.6f, 0.2f), 22f, 20f);
            fx.flashDuration = 0.3f;
            return Save(root, "Fx_Explosion");
        }

        internal static ParticleSystem Burst(GameObject parent, string name, Material mat, int count, float life0, float life1, float speed0, float speed1,
            float size0, float size1, Color c0, Color c1, float gravity, ParticleSystemShapeType shape, float shapeSize, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed0, speed1);
            main.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.gravityModifier = gravity;
            main.maxParticles = count + 4;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = shape;
            if (shape == ParticleSystemShapeType.Cone) { sh.angle = shapeSize; sh.radius = 0.05f; }
            else sh.radius = shapeSize;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Grad(new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f)));

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.1f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 2.5f;
                r.velocityScale = 0.05f;
            }
            return ps;
        }

        internal static Light AddLight(GameObject go, Color color, float range, float intensity)
        {
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        internal static PooledFx Save(GameObject root, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<PooledFx>();
        }

        internal static Gradient Grad(Color from, Color to)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                      new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(to.a, 1f) });
            return g;
        }

        // ------------------------------------------------------------------ geometry / materials / textures

        internal static Transform Block(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        internal static Transform Quad(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        internal static Transform Spawn(string name, Transform parent, Vector3 pos, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return t;
        }

        internal static Material Lit(string name, Color color, Texture2D baseMap = null, float tiling = 1f)
        {
            var m = NewMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.25f);
            m.SetFloat("_Metallic", 0f);
            if (baseMap != null)
            {
                m.SetTexture("_BaseMap", baseMap);
                m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                m.SetColor("_BaseColor", Color.white);
            }
            return m;
        }

        internal static Material Unlit(string name, Color color)
        {
            var m = NewMaterial(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            return m;
        }

        internal static Material Particle(string name, Texture2D tex, bool additive)
        {
            var m = NewMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);                         // transparent
            m.SetFloat("_Blend", additive ? 2f : 0f);           // additive / alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        internal static Material NewMaterial(string name, string shaderName)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Shader s = Shader.Find(shaderName);
            if (s == null) throw new System.Exception("Shader not found: " + shaderName);
            // reuse an existing asset in place: deleting and recreating it would orphan every earlier reference to it
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                var fresh = new Material(s);
                existing.shader = s;
                existing.CopyPropertiesFromMaterial(fresh);
                Object.DestroyImmediate(fresh);
                return existing;
            }
            var m = new Material(s) { name = name };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        internal static Texture2D MakeSoftCircle()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            return SaveTexture(tex, "SoftCircle", false);
        }

        internal static Texture2D MakeGrid()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            Color bg = new Color(0.13f, 0.15f, 0.19f), line = new Color(0.24f, 0.28f, 0.34f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    tex.SetPixel(x, y, (x < 2 || y < 2) ? line : bg);
            return SaveTexture(tex, "Grid", true);
        }

        internal static Texture2D SaveTexture(Texture2D tex, string name, bool repeat)
        {
            string path = Root + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.anisoLevel = repeat ? 8 : 1;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = repeat;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }
    }
}
