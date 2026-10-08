using System.Collections.Generic;
using System.IO;
using System.Linq;
using Reflex.Core;
using Tank.Core.Hit;
using Tank.Gameplay;
using Tank.Gameplay.Arena;
using Tank.Gameplay.CameraSystem;
using Tank.Core.Items;
using Tank.Gameplay.Config;
using Tank.Gameplay.Cores;
using Tank.Gameplay.Items;
using Unity.Cinemachine;
using Tank.Gameplay.Flow;
using Tank.Gameplay.Fx;
using Tank.Gameplay.Views;
using Tank.Net;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using Tank.Gameplay.UI;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Tank.Editor
{
    /// <summary>
    /// Builds everything the match scene needs from the weapon pack's assets: configs, sound cues, clean effect and projectile prefabs, the tank
    /// view, the arena and the scene itself. Re-running it rebuilds in place, so the pack stays untouched and the content is reproducible.
    /// </summary>
    public static class GameContentBuilder
    {
        const string Root = "Assets/Game/Content";
        const string ScenePath = "Assets/Scenes/Game_Arena.unity";
        const string HostAddress = "192.168.1.15";        // the machine that hosts by default; the Join box starts with it
        const float CameraFov = 15f;                          // Tanknarok's telephoto lens: a flat, close-in look from far away
        static readonly Vector3 CameraStart = new Vector3(0f, Mathf.Sin(55f * Mathf.Deg2Rad), -Mathf.Cos(55f * Mathf.Deg2Rad)) * 35f;    // 55 degrees down at the minimum distance
        const float MapScale = 1f;                 // the pack's arena is sized for 1.4 m tanks; the match wants room to move

        [MenuItem("Tank/Game/Build Content And Match Scene")]
        public static void BuildAll()
        {
            foreach (string f in new[] { "Config", "Audio", "Materials", "Prefabs", "Prefabs/Fx", "Prefabs/Projectiles", "Prefabs/Tank", "Prefabs/Arena", "Prefabs/Net", "Prefabs/Items" }) EnsureFolder(Root + "/" + f);

            EnsureReflexSettings();
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixer/Master.mixer");
            AudioMixerGroup sfxGroup = Group(mixer, "sfx", "fx"), musicGroup = Group(mixer, "music");

            var cues = BuildCues(sfxGroup);
            var fx = BuildFxPrefabs();
            var projectiles = BuildProjectiles();
            WeaponConfig weapons = BuildWeapons(cues, fx, projectiles);
            ItemConfig itemConfig = BuildItems(cues, fx);
            CoreConfig coreConfig = BuildCores();
            ItemSlotView itemSlotPrefab = BuildItemSlotPrefab();
            FxConfig fxConfig = BuildFxConfig(cues, fx, musicGroup);
            TankView tankPrefab = BuildTankView(fx);
            PlayerPalette palette = BuildPalette();
            TankConfig tank = Asset<TankConfig>(Root + "/Config/TankConfig.asset");
            MatchConfig match = Asset<MatchConfig>(Root + "/Config/MatchConfig.asset");
            ArenaConfig arena = BuildArena(out GameObject arenaPrefab);
            foreach (Object o in new Object[] { tank, match }) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            BuildNetPrefabs();
            BuildScene(tank, weapons, match, arena, palette, fxConfig, itemConfig, coreConfig, itemSlotPrefab, tankPrefab, arenaPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Game content built: scene " + ScenePath);
        }

        // ------------------------------------------------------------------ sound

        static Dictionary<string, SfxCue> BuildCues(AudioMixerGroup group)
        {
            var cues = new Dictionary<string, SfxCue>();
            void Cue(string key, float volume, float pitchVar, params string[] folders)
            {
                SfxCue cue = Asset<SfxCue>(Root + "/Audio/" + key + ".asset");
                cue.clips = folders.SelectMany(f => AssetDatabase.FindAssets("t:AudioClip", new[] { f })).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                    .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                cue.volume = volume; cue.pitchVariation = pitchVar; cue.mixerGroup = group;
                if (cue.clips.Length == 0) Debug.LogWarning("No clips for cue " + key);
                EditorUtility.SetDirty(cue);
                cues[key] = cue;
            }
            Cue("shoot_main", 0.8f, 0.08f, "Assets/Audio/Weapon/MainWeapon");
            Cue("shoot_minigun", 0.55f, 0.12f, "Assets/Audio/Weapon/Minigun");
            Cue("shoot_gigavolt", 0.9f, 0.06f, "Assets/Audio/Weapon/GigaVolt");
            Cue("shoot_grenade", 0.9f, 0.08f, "Assets/Audio/Weapon/Grenades");
            Cue("exp_medium", 0.85f, 0.1f, "Assets/Audio/Explosion/Medium");
            Cue("exp_large", 1f, 0.08f, "Assets/Audio/Explosion/Large");
            Cue("exp_gigavolt", 1f, 0.06f, "Assets/Audio/Explosion/GigaVolt");
            Cue("tank_hit", 0.8f, 0.1f, "Assets/Audio/Tank/Hit");
            Cue("tank_explosion", 1f, 0.05f, "Assets/Audio/Tank/Explosion");
            Cue("tank_ready", 0.7f, 0.05f, "Assets/Audio/Tank/Ready");
            Cue("teleport_in", 0.8f, 0.05f, "Assets/Audio/Tank/Teleport");
            foreach (string misc in new[] { "countdown", "celebration", "score_change", "transition" })
            {
                SfxCue cue = Asset<SfxCue>(Root + "/Audio/misc_" + misc + ".asset");
                cue.clips = AssetDatabase.FindAssets("t:AudioClip sx_misc_" + misc, new[] { "Assets/Audio/Misc" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                cue.volume = 0.9f; cue.pitchVariation = 0f; cue.mixerGroup = group;
                EditorUtility.SetDirty(cue);
                cues["misc_" + misc] = cue;
            }
            return cues;
        }

        static AudioMixerGroup Group(AudioMixer mixer, params string[] hints)
        {
            if (mixer == null) return null;
            AudioMixerGroup[] all = mixer.FindMatchingGroups(string.Empty);
            foreach (string h in hints) foreach (AudioMixerGroup g in all) if (g.name.ToLowerInvariant().Contains(h)) return g;
            return all.Length > 0 ? all[0] : null;
        }

        // ------------------------------------------------------------------ effects and projectiles

        static Dictionary<string, GameObject> BuildFxPrefabs()
        {
            var fx = new Dictionary<string, GameObject>();
            void One(string key, string path) { fx[key] = CleanCopy(path, "Fx_" + key, Root + "/Prefabs/Fx/Fx_" + key + ".prefab"); }
            One("muzzle_default", "Assets/Prefabs/FX/MuzzleFlash/pfx_mzf_Default.prefab");
            One("muzzle_minigun", "Assets/Prefabs/FX/MuzzleFlash/pfx_mzf_Minigun.prefab");
            One("muzzle_gigavolt", "Assets/Prefabs/FX/MuzzleFlash/pfx_mzf_GigaVolt.prefab");
            One("muzzle_grenade", "Assets/Prefabs/FX/MuzzleFlash/pfx_mzf_Grenade.prefab");
            One("exp_medium", "Assets/Prefabs/FX/Explosions/pfx_exp_Medium.prefab");
            One("exp_large", "Assets/Prefabs/FX/Explosions/pfx_exp_Large.prefab");
            One("exp_gigavolt", "Assets/Prefabs/FX/Explosions/pfx_exp_GigaVolt.prefab");
            One("exp_minigun", "Assets/Prefabs/FX/Explosions/pfx_exp_Minigun.prefab");
            One("tank_explosion", "Assets/Prefabs/FX/Explosions/pfx_exp_TankExplosion.prefab");
            One("tank_parts", "Assets/Prefabs/FX/Tank/Damage/pfx_exp_Tank parts.prefab");
            One("tank_hit", "Assets/Prefabs/FX/Tank/Damage/pfx_exp_Tank Damage 01.prefab");
            One("dust", "Assets/Prefabs/FX/Tank/DriveDust/pfx_DrivingDust Normal.prefab");
            One("item_pickup", "Assets/Prefabs/WeaponFX/pfx_spark_LaserSightImpact.prefab");
            One("core_picked", "Assets/Prefabs/FX/Tank/Teleport/pfx_Teleport_Discharge.prefab");
            fx["dust_0"] = CleanCopy("Assets/Prefabs/FX/Tank/DriveDust/pfx_DrivingDust Normal.prefab", "Dust_normal", Root + "/Prefabs/Fx/Dust_normal.prefab", true);
            fx["dust_1"] = CleanCopy("Assets/Prefabs/FX/Tank/DriveDust/pfx_DrivingDust Damaged.prefab", "Dust_damaged", Root + "/Prefabs/Fx/Dust_damaged.prefab", true);
            fx["dust_2"] = CleanCopy("Assets/Prefabs/FX/Tank/DriveDust/pfx_DrivingDust Critical.prefab", "Dust_critical", Root + "/Prefabs/Fx/Dust_critical.prefab", true);
            fx["spawn"] = Composite("Fx_spawn", Root + "/Prefabs/Fx/Fx_spawn.prefab", "Assets/Prefabs/FX/Tank/Teleport/pfx_Teleport_BeamDown.prefab", "Assets/Prefabs/FX/Tank/Teleport/pfx_Teleport_Discharge.prefab");
            return fx;
        }

        /// <summary>An effect from the pack with everything gameplay-specific removed (its own scripts, audio and colliders), saved under Game/Content.</summary>
        static GameObject CleanCopy(string sourcePath, string name, string targetPath, bool playOnAwake = false)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (src == null) { Debug.LogWarning("Missing effect " + sourcePath); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            Clean(go, playOnAwake);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, targetPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Composite(string name, string targetPath, params string[] sources)
        {
            var root = new GameObject(name);
            foreach (string s in sources)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(s);
                if (src == null) continue;
                var part = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                PrefabUtility.UnpackPrefabInstance(part, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            Clean(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void Clean(GameObject root, bool playOnAwake = false)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
            foreach (AudioSource a in root.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody r in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.playOnAwake = playOnAwake; m.stopAction = ParticleSystemStopAction.None; }
        }

        static GameObject[] BuildProjectiles()
        {
            var list = new GameObject[6];
            list[0] = CleanCopy("Assets/Prefabs/Shots/shot_default_visual.prefab", "Shot_cannon", Root + "/Prefabs/Projectiles/Shot_cannon.prefab");
            list[1] = CleanCopy("Assets/Prefabs/Shots/shot_minigun_visual.prefab", "Shot_minigun", Root + "/Prefabs/Projectiles/Shot_minigun.prefab");
            list[5] = CleanCopy("Assets/Prefabs/Shots/shot_grenade_visual.prefab", "Shot_grenade", Root + "/Prefabs/Projectiles/Shot_grenade.prefab");
            // the pack has no ready-made shot for these three: they are built from its projectile meshes
            list[2] = MeshShot("Shot_shotgun", "bullet05", 2.5f, new Color(1f, 0.6f, 0.2f), false);
            list[3] = MeshShot("Shot_rocket", "missile2", 3.2f, new Color(1f, 0.45f, 0.2f), true);
            list[4] = MeshShot("Shot_gigavolt", "bullet03", 4.5f, new Color(0.4f, 0.85f, 1f), true);
            return list;
        }

        static GameObject MeshShot(string name, string meshName, float scale, Color color, bool trail)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/projectiles.fbx");
            Transform part = fbx.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == meshName);
            var root = new GameObject(name);
            if (part != null)
            {
                var mesh = new GameObject("Mesh"); mesh.transform.SetParent(root.transform, false);
                mesh.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                mesh.AddComponent<MeshRenderer>().sharedMaterial = TintedMaterial(name, color);
                mesh.transform.localScale = Vector3.one * scale;
            }
            else Debug.LogWarning("Projectile mesh not found: " + meshName);
            if (trail)
            {
                var tr = root.AddComponent<TrailRenderer>();
                tr.time = 0.25f; tr.widthMultiplier = 0.25f; tr.startColor = color; tr.endColor = new Color(color.r, color.g * 0.5f, 0.1f, 0f);
                tr.sharedMaterial = TrailMaterial();
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Projectiles/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Material TintedMaterial(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Weapons/me_DefaultBullet.mat");
                m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 2f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material TrailMaterial()
        {
            string path = Root + "/Materials/Trail.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        // ------------------------------------------------------------------ configs

        static WeaponConfig BuildWeapons(Dictionary<string, SfxCue> c, Dictionary<string, GameObject> fx, GameObject[] shots)
        {
            WeaponConfig w = Asset<WeaponConfig>(Root + "/Config/WeaponConfig.asset");
            WeaponConfig.Entry E(string name, int dmg, float interval, float speed, float range, int pellets, float spread, int ammo, float splashR, float splashF, int shot, string muzzle, string impact, string shootSfx, string impactSfx)
            {
                return new WeaponConfig.Entry
                {
                    name = name, damage = dmg, interval = interval, projectileSpeed = speed, range = range, pellets = pellets, spreadDegrees = spread, ammo = ammo, splashRadius = splashR, splashFactor = splashF,
                    projectilePrefab = shots[shot], muzzleVfx = fx[muzzle], impactVfx = fx[impact], shootSfx = c[shootSfx], impactSfx = c[impactSfx],
                };
            }
            w.weapons = new[]
            {
                E("CANNON", 25, 0.35f, 40f, 45f, 1, 0f, 0, 0f, 0f, 0, "muzzle_default", "exp_medium", "shoot_main", "exp_medium"),
                E("MINIGUN", 8, 0.09f, 55f, 35f, 1, 3.5f, 45, 0f, 0f, 1, "muzzle_minigun", "exp_minigun", "shoot_minigun", "exp_medium"),
                E("SHOTGUN", 9, 0.75f, 32f, 20f, 7, 18f, 7, 0f, 0f, 2, "muzzle_default", "exp_minigun", "shoot_main", "exp_medium"),
                E("ROCKET", 55, 1f, 24f, 50f, 1, 0f, 4, 4.5f, 0.6f, 3, "muzzle_grenade", "exp_large", "shoot_grenade", "exp_large"),
                E("GIGAVOLT", 35, 0.9f, 110f, 45f, 1, 0f, 5, 0f, 0f, 4, "muzzle_gigavolt", "exp_gigavolt", "shoot_gigavolt", "exp_gigavolt"),
                E("GRENADE", 40, 1.2f, 20f, 26f, 1, 0f, 5, 3.5f, 0.6f, 5, "muzzle_grenade", "exp_large", "shoot_grenade", "exp_large"),
            };
            EditorUtility.SetDirty(w);
            return w;
        }

        // ------------------------------------------------------------------ items and cores

        static GameObject IconPrefab(string name, string meshName, float scale)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/powerup_icons.fbx");
            Transform part = fbx.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == meshName);
            var root = new GameObject(name);
            if (part != null)
            {
                var mesh = new GameObject("Icon"); mesh.transform.SetParent(root.transform, false);
                mesh.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                mesh.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Palettes/mpe_PowerupIcons.mat");
                mesh.transform.localScale = Vector3.one * scale;
            }
            else Debug.LogWarning("Icon mesh not found: " + meshName);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Items/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static ItemConfig BuildItems(Dictionary<string, SfxCue> cues, Dictionary<string, GameObject> fx)
        {
            ItemConfig cfg = Asset<ItemConfig>(Root + "/Config/ItemConfig.asset");
            ItemConfig.Entry E(string name, ItemEffectKind kind, float magnitude, float duration, int weapon, float weight, float respawn, string icon, Color color)
            {
                return new ItemConfig.Entry { name = name, kind = kind, magnitude = magnitude, duration = duration, weaponId = weapon, weight = weight, respawnSeconds = respawn,
                    iconPrefab = IconPrefab("Icon_" + name, icon, 2.2f), color = color, pickupVfx = fx["item_pickup"], pickupSfx = cues["tank_ready"] };
            }
            cfg.items = new[]
            {
                E("REPAIR", ItemEffectKind.Repair, 40f, 0f, 0, 3f, 25f, "wrench", new Color(0.35f, 1f, 0.5f)),
                E("SHIELD", ItemEffectKind.Shield, 50f, 12f, 0, 2f, 30f, "shield", new Color(0.3f, 0.85f, 1f)),
                E("SPEED", ItemEffectKind.Speed, 0f, 8f, 0, 2f, 30f, "Superspeed", new Color(1f, 0.9f, 0.25f)),
                E("DAMAGE", ItemEffectKind.Damage, 0f, 10f, 0, 1.5f, 40f, "crosshair", new Color(1f, 0.3f, 0.25f)),
                E("MINIGUN", ItemEffectKind.Weapon, 0f, 0f, 1, 1.5f, 35f, "yp_minigun", new Color(1f, 0.65f, 0.15f)),
                E("SHOTGUN", ItemEffectKind.Weapon, 0f, 0f, 2, 1.5f, 35f, "yp_scattershot", new Color(0.95f, 0.35f, 0.9f)),
                E("ROCKET", ItemEffectKind.Weapon, 0f, 0f, 3, 1f, 45f, "missiles", new Color(0.7f, 0.5f, 1f)),
                E("GIGAVOLT", ItemEffectKind.Weapon, 0f, 0f, 4, 1f, 40f, "ye_lightningbeam", new Color(0.4f, 0.8f, 1f)),
                E("GRENADE", ItemEffectKind.Weapon, 0f, 0f, 5, 1f, 40f, "grenade", new Color(0.6f, 0.9f, 0.3f)),
            };
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        /// <summary>The ten cores of the design (the two that only made sense with flags are left out).</summary>
        static CoreConfig BuildCores()
        {
            CoreConfig cfg = Asset<CoreConfig>(Root + "/Config/CoreConfig.asset");
            CoreConfig.Entry C(string name, string text, Color color, int hp = 0, float speed = 1f, float dmg = 1f, float fire = 1f, float ammo = 1f, float dash = 1f, float splash = 0f, float steal = 0f, float regen = 0f)
            {
                return new CoreConfig.Entry { name = name, description = text, color = color, maxHpBonus = hp, speedMult = speed, damageMult = dmg, fireIntervalMult = fire, ammoMult = ammo, dashCooldownMult = dash, splashBonus = splash, lifesteal = steal, regenPerSecond = regen };
            }
            cfg.cores = new[]
            {
                C("Heavy Plating", "+40 max HP, 8% slower", new Color(0.55f, 0.75f, 1f), hp: 40, speed: 0.92f),
                C("Overdrive Engine", "+18% move speed", new Color(1f, 0.85f, 0.3f), speed: 1.18f),
                C("Heavy Shells", "+30% damage, 15% slower fire", new Color(1f, 0.5f, 0.3f), dmg: 1.3f, fire: 1.15f),
                C("Rapid Loader", "22% faster fire, -10% damage", new Color(1f, 0.95f, 0.5f), dmg: 0.9f, fire: 0.78f),
                C("Quick Hands", "+40% ammo from weapon pickups", new Color(0.7f, 1f, 0.7f), ammo: 1.4f),
                C("Blast Rounds", "Shells explode: +1.8 m splash", new Color(1f, 0.6f, 0.2f), splash: 1.8f),
                C("Afterburner", "Dash cooldown -45%", new Color(0.6f, 0.9f, 1f), dash: 0.55f),
                C("Repair Nanites", "Regenerate 4 HP/s out of combat", new Color(0.45f, 1f, 0.6f), regen: 4f),
                C("Vampiric Rounds", "Heal 25% of damage dealt", new Color(1f, 0.4f, 0.5f), steal: 0.25f),
                C("Glass Cannon", "+50% damage, -25 max HP", new Color(1f, 0.35f, 0.35f), hp: -25, dmg: 1.5f),
            };
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        static ItemSlotView BuildItemSlotPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/powerup_icons.fbx");
            Transform pad = fbx.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "platform");
            var root = new GameObject("ItemSlot");
            if (pad != null)
            {
                var padGo = new GameObject("Pad"); padGo.transform.SetParent(root.transform, false);
                padGo.AddComponent<MeshFilter>().sharedMesh = pad.GetComponent<MeshFilter>().sharedMesh;
                padGo.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Palettes/mpe_PowerupIcons.mat");
                padGo.transform.localScale = Vector3.one * 2.4f;
            }
            var anchor = new GameObject("IconAnchor"); anchor.transform.SetParent(root.transform, false); anchor.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var view = root.AddComponent<ItemSlotView>();
            var so = new SerializedObject(view); so.FindProperty("iconAnchor").objectReferenceValue = anchor.transform; so.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Items/ItemSlot.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<ItemSlotView>();
        }

        static FxConfig BuildFxConfig(Dictionary<string, SfxCue> c, Dictionary<string, GameObject> fx, AudioMixerGroup musicGroup)
        {
            FxConfig f = Asset<FxConfig>(Root + "/Config/FxConfig.asset");
            f.tankHitVfx = fx["tank_hit"]; f.tankExplosionVfx = fx["tank_explosion"]; f.tankDebrisVfx = fx["tank_parts"]; f.spawnVfx = fx["spawn"]; f.dashVfx = fx["dust"];
            f.tankHitSfx = c["tank_hit"]; f.tankExplosionSfx = c["tank_explosion"]; f.spawnSfx = c["teleport_in"]; f.readySfx = c["tank_ready"];
            f.corePickedVfx = fx["core_picked"]; f.corePickedSfx = c["tank_ready"]; f.coreOfferedSfx = c["misc_transition"];
            f.countdownSfx = c["misc_countdown"]; f.celebrationSfx = c["misc_celebration"]; f.scoreChangeSfx = c["misc_score_change"];
            string musicPath = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Music" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            f.music = musicPath != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(musicPath) : null;
            f.musicGroup = musicGroup;
            EditorUtility.SetDirty(f);
            return f;
        }

        static PlayerPalette BuildPalette()
        {
            PlayerPalette p = Asset<PlayerPalette>(Root + "/Config/PlayerPalette.asset");
            Material M(int n) { return AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlayerColors/mp_tank_player_0" + n + ".mat"); }
            // the pack's four player materials: 01 red, 02 blue, 03 yellow (duckling), 04 green (froggy)
            p.slots = new[]
            {
                new PlayerPalette.Slot { name = "RED", color = new Color(1f, 0.35f, 0.3f), tankMaterial = M(1) },
                new PlayerPalette.Slot { name = "BLUE", color = new Color(0.3f, 0.65f, 1f), tankMaterial = M(2) },
                new PlayerPalette.Slot { name = "YELLOW", color = new Color(1f, 0.85f, 0.25f), tankMaterial = M(3) },
                new PlayerPalette.Slot { name = "GREEN", color = new Color(0.45f, 0.9f, 0.4f), tankMaterial = M(4) },
            };
            EditorUtility.SetDirty(p);
            return p;
        }

        // ------------------------------------------------------------------ tank and arena

        /// <summary>
        /// The tank prefab, assembled from single-purpose components: a model (TankVisual), where it stands (TankMotionView), its colour
        /// (TankColorView), its bar (TankHealthBarView) and where it is solid (TankHitbox), tied together by the thin TankView.
        /// </summary>
        static TankView BuildTankView(Dictionary<string, GameObject> fx)
        {
            var pack = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Game/PlayerTank.prefab"));
            PrefabUtility.UnpackPrefabInstance(pack, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Transform hull = pack.transform.Find("TankParent");
            Transform turret = hull.Find("visual_turret");
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/weapon_primary_main.prefab"));
            PrefabUtility.UnpackPrefabInstance(weapon, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Transform muzzle = weapon.transform.Find("Exits/Exit");
            weapon.transform.SetParent(turret.Find("PrimaryWeapons"), false);
            weapon.transform.localPosition = Vector3.zero; weapon.transform.localRotation = Quaternion.identity;

            var root = new GameObject("TankView");
            var visualGo = new GameObject("Visual"); visualGo.transform.SetParent(root.transform, false);
            hull.SetParent(visualGo.transform, false);
            Object.DestroyImmediate(pack);                       // the pack's teleport effects, damage particles and spare weapons stay behind
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (Animator an in root.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);

            // health bar on its own object
            var bar = new GameObject("HealthBar"); bar.transform.SetParent(root.transform, false); bar.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            Quad("Back", bar.transform, new Vector3(1.7f, 0.22f, 1f), 0f, UnlitMaterial("HealthBack", new Color(0f, 0f, 0f, 0.7f)));
            Transform fill = Quad("Fill", bar.transform, new Vector3(1.6f, 0.14f, 1f), -0.01f, UnlitMaterial("HealthFill", new Color(0.4f, 1f, 0.5f)));

            var visual = visualGo.AddComponent<TankVisual>();
            var visualSo = new SerializedObject(visual);
            visualSo.FindProperty("turret").objectReferenceValue = turret;
            visualSo.FindProperty("body").objectReferenceValue = hull.gameObject;
            visualSo.ApplyModifiedPropertiesWithoutUndo();

            var colorView = root.AddComponent<TankColorView>();
            colorView.CollectRenderers();

            var healthBar = bar.AddComponent<TankHealthBarView>();
            var barSo = new SerializedObject(healthBar);
            barSo.FindProperty("fill").objectReferenceValue = fill; barSo.FindProperty("fillWidth").floatValue = 1.6f;
            barSo.ApplyModifiedPropertiesWithoutUndo();

            var motion = root.AddComponent<TankMotionView>();
            var motionSo = new SerializedObject(motion); motionSo.FindProperty("visual").objectReferenceValue = visual; motionSo.ApplyModifiedPropertiesWithoutUndo();

            var hitbox = root.AddComponent<TankHitbox>();
            hitbox.collisionRadius = 1f; hitbox.hitRadius = 0.95f; hitbox.muzzle = muzzle;

            // how battered it looks, the dust it kicks up, and what its buffs look like: one component each
            var dmg = BuildDamageView(root, hull, turret);
            var dust = BuildDustView(root, hull, fx);
            var status = BuildStatusView(root, turret);
            var nameView = BuildNameView(root);
            var localMarker = BuildLocalMarker(root);

            var view = root.AddComponent<TankView>();
            var so = new SerializedObject(view);
            so.FindProperty("nameView").objectReferenceValue = nameView; so.FindProperty("localMarker").objectReferenceValue = localMarker; so.FindProperty("damage").objectReferenceValue = dmg; so.FindProperty("dust").objectReferenceValue = dust; so.FindProperty("status").objectReferenceValue = status;
            so.FindProperty("motion").objectReferenceValue = motion; so.FindProperty("visual").objectReferenceValue = visual; so.FindProperty("color").objectReferenceValue = colorView;
            so.FindProperty("healthBar").objectReferenceValue = healthBar; so.FindProperty("hitbox").objectReferenceValue = hitbox;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Tank/TankView.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<TankView>();
        }

        static TankLocalMarker BuildLocalMarker(GameObject root)
        {
            var holder = new GameObject("LocalMarker"); holder.transform.SetParent(root.transform, false); holder.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            var ringGo = new GameObject("Ring"); ringGo.transform.SetParent(holder.transform, false);
            ringGo.AddComponent<MeshFilter>();
            var mr = ringGo.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            const string matPath = Root + "/Materials/LocalMarker.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { Directory.CreateDirectory(Root + "/Materials"); mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, matPath); }
            mr.sharedMaterial = mat;
            var marker = holder.AddComponent<TankLocalMarker>();
            var so = new SerializedObject(marker); so.FindProperty("ring").objectReferenceValue = mr; so.ApplyModifiedPropertiesWithoutUndo();
            return marker;
        }

        static TankNameView BuildNameView(GameObject root)
        {
            var go = new GameObject("Name"); go.transform.SetParent(root.transform, false); go.transform.localPosition = new Vector3(0f, 2.9f, 0f);
            var label = go.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.fontSize = 64; label.characterSize = 0.075f; label.fontStyle = FontStyle.Bold; label.text = "Player";
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            var view = go.AddComponent<TankNameView>();
            var so = new SerializedObject(view); so.FindProperty("label").objectReferenceValue = label; so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        static TankDamageView BuildDamageView(GameObject root, Transform hull, Transform turret)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/player_tank.fbx");
            Mesh M(string n) { Transform t = fbx.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n); return t != null ? t.GetComponent<MeshFilter>().sharedMesh : null; }
            var view = root.AddComponent<TankDamageView>();
            var so = new SerializedObject(view);
            // "hull" is the pack's TankParent group; the meshes themselves are on its visual_hull and visual_turret children
            so.FindProperty("hull").objectReferenceValue = hull.Find("visual_hull").GetComponent<MeshFilter>();
            so.FindProperty("turret").objectReferenceValue = turret.GetComponent<MeshFilter>();
            Mesh[] hulls = { M("hull02"), M("hull02_damaged"), M("hull02_critical") }, turrets = { M("turret01"), M("turret01_damaged"), M("turret01_critical") };
            SerializedProperty hp = so.FindProperty("hullStages"), tp = so.FindProperty("turretStages");
            hp.arraySize = 3; tp.arraySize = 3;
            for (int i = 0; i < 3; i++) { hp.GetArrayElementAtIndex(i).objectReferenceValue = hulls[i]; tp.GetArrayElementAtIndex(i).objectReferenceValue = turrets[i]; }
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        static TankDustView BuildDustView(GameObject root, Transform hull, Dictionary<string, GameObject> fx)
        {
            var holder = new GameObject("Dust"); holder.transform.SetParent(root.transform, false);
            var view = holder.AddComponent<TankDustView>();
            var so = new SerializedObject(view);
            SerializedProperty list = so.FindProperty("stageEmitters"); list.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                var e = (GameObject)PrefabUtility.InstantiatePrefab(fx["dust_" + i], holder.transform);
                e.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                e.SetActive(i == 0);
                list.GetArrayElementAtIndex(i).objectReferenceValue = e;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        static TankStatusView BuildStatusView(GameObject root, Transform turret)
        {
            var holder = new GameObject("Status"); holder.transform.SetParent(root.transform, false);
            var view = holder.AddComponent<TankStatusView>();

            var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(bubble.GetComponent<Collider>());
            bubble.name = "ShieldBubble"; bubble.transform.SetParent(holder.transform, false); bubble.transform.localPosition = new Vector3(0f, 0.7f, 0f); bubble.transform.localScale = Vector3.one * 3.2f;
            bubble.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FX/mfx_forceField.mat");
            bubble.SetActive(false);

            var trails = new TrailRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var t = new GameObject(i == 0 ? "TrailL" : "TrailR"); t.transform.SetParent(holder.transform, false); t.transform.localPosition = new Vector3(i == 0 ? -0.55f : 0.55f, 0.3f, -0.6f);
                var tr = t.AddComponent<TrailRenderer>();
                tr.time = 0.45f; tr.widthMultiplier = 0.45f; tr.startColor = new Color(1f, 0.9f, 0.25f, 0.9f); tr.endColor = new Color(1f, 0.6f, 0.1f, 0f); tr.sharedMaterial = TrailMaterial(); tr.emitting = false;
                trails[i] = tr;
            }

            var embers = new GameObject("DamageEmbers"); embers.transform.SetParent(turret, false); embers.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var ps = embers.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.startLifetime = 0.7f; main.startSpeed = 1.2f; main.startSize = 0.22f; main.startColor = new Color(1f, 0.35f, 0.15f, 1f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.3f;
            var em = ps.emission; em.rateOverTime = 22f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.7f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = TrailMaterial();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var so = new SerializedObject(view);
            so.FindProperty("shieldBubble").objectReferenceValue = bubble;
            SerializedProperty tl = so.FindProperty("speedTrails"); tl.arraySize = 2; for (int i = 0; i < 2; i++) tl.GetArrayElementAtIndex(i).objectReferenceValue = trails[i];
            so.FindProperty("damageEmbers").objectReferenceValue = ps;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        static Transform Quad(string name, Transform parent, Vector3 scale, float z, Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(parent, false); q.transform.localPosition = new Vector3(0f, 0f, z); q.transform.localScale = scale;
            q.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return q.transform;
        }

        static Material UnlitMaterial(string name, Color c)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            if (c.a < 1f)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        struct CoverDef { public string model; public float x, z, yaw; public CoverDef(string m, float x, float z, float yaw) { model = m; this.x = x; this.z = z; this.yaw = yaw; } }

        /// <summary>
        /// The arena prefab, with what you see kept apart from what is solid: Visuals (meshes), Hitboxes (HitboxBlock components, drawn as
        /// gizmos) and SpawnPoints. The ArenaConfig the rules load is baked from the components, never typed in.
        /// </summary>
        static ArenaConfig BuildArena(out GameObject arenaPrefab)
        {
            var map = new GameObject("Arena_Laser");
            map.AddComponent<ArenaAuthoring>().playableSemiAxes = new Vector2(26.5f, 17.75f) * MapScale;        // the pack's arena at scale 1, so the playable area follows the visuals

            var visuals = new GameObject("Visuals"); visuals.transform.SetParent(map.transform, false);
            Mesh(visuals.transform, "Assets/Models/laser_arenaplatform_combined.fbx", "platform01_combined", "Platform", new Vector3(1.1f, 0f, -0.2f) * MapScale, MapScale, "Assets/Materials/Special/mg_LaserGround.mat");
            Mesh(visuals.transform, "Assets/Models/barrieroval01.fbx", "barrier_oval01", "Rim", Vector3.zero, MapScale, "Assets/Materials/FX/mfx_forceField.mat");
            var lava = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(lava.GetComponent<Collider>());
            lava.name = "Lava"; lava.transform.SetParent(visuals.transform, false);
            lava.transform.SetPositionAndRotation(new Vector3(0f, -4.2f, 0f), Quaternion.Euler(90f, 0f, 0f)); lava.transform.localScale = new Vector3(300f, 300f, 1f);
            lava.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FX/mfx_LaserLava.mat");

            var defs = new[]
            {
                new CoverDef("lava_rock01", 8f, 6f, 20f), new CoverDef("lava_rock01", -8f, -6f, 20f), new CoverDef("lava_rock01", 8f, -6f, -20f), new CoverDef("lava_rock01", -8f, 6f, -20f),
                new CoverDef("lava_rock04", 16f, 10f, 0f), new CoverDef("lava_rock04", -16f, -10f, 0f), new CoverDef("lava_rock04", 16f, -10f, 90f), new CoverDef("lava_rock04", -16f, 10f, 90f),
                new CoverDef("lava_pillar01", 3f, 8f, 0f), new CoverDef("lava_pillar02", -3f, 8f, 0f), new CoverDef("lava_pillar01", -3f, -8f, 0f), new CoverDef("lava_pillar02", 3f, -8f, 0f),
                new CoverDef("lava_rock02", 0f, 13f, 0f), new CoverDef("lava_rock02", 0f, -13f, 0f), new CoverDef("lava_rock03", 24f, 0f, 0f), new CoverDef("lava_rock03", -24f, 0f, 0f),
            };
            var cover = new GameObject("Cover"); cover.transform.SetParent(visuals.transform, false);
            var hitboxes = new GameObject("Hitboxes"); hitboxes.transform.SetParent(map.transform, false);
            for (int i = 0; i < defs.Length; i++)
            {
                Vector3 pos = new Vector3(defs[i].x, 0f, defs[i].z) * MapScale;
                GameObject c = Mesh(cover.transform, "Assets/Models/laser_blockers.fbx", defs[i].model, "Cover_" + i, pos, 1f, "Assets/Materials/Palettes/mp_LaserTheme.mat");
                c.transform.rotation = Quaternion.Euler(0f, defs[i].yaw, 0f);

                // the hitbox starts as the mesh's footprint, then belongs to whoever edits the gizmo
                Bounds lb = c.GetComponent<MeshFilter>().sharedMesh.bounds;
                var hb = new GameObject("Hitbox_" + i); hb.transform.SetParent(hitboxes.transform, false);
                hb.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, defs[i].yaw, 0f));
                var block = hb.AddComponent<HitboxBlock>();
                block.halfExtents = new Vector2(lb.extents.x, lb.extents.z); block.centerOffset = new Vector2(lb.center.x, lb.center.z);
            }

            var spawnRoot = new GameObject("SpawnPoints"); spawnRoot.transform.SetParent(map.transform, false);
            Vector2[] baseSpawns = { new Vector2(-21f, -6f), new Vector2(-21f, 6f), new Vector2(21f, -6f), new Vector2(21f, 6f), new Vector2(-6f, -14f), new Vector2(6f, -14f), new Vector2(-6f, 14f), new Vector2(6f, 14f) };
            for (int i = 0; i < baseSpawns.Length; i++)
            {
                var sp = new GameObject("Spawn_" + i); sp.transform.SetParent(spawnRoot.transform, false);
                sp.transform.position = new Vector3(baseSpawns[i].x, 0f, baseSpawns[i].y) * MapScale;
                sp.AddComponent<SpawnPointMarker>();
            }

            var slotRoot = new GameObject("ItemSlots"); slotRoot.transform.SetParent(map.transform, false);
            Vector2[] baseSlots = { new Vector2(-12f, 0f), new Vector2(12f, 0f), new Vector2(0f, 0f), new Vector2(-10f, -13f), new Vector2(10f, -13f), new Vector2(-10f, 13f), new Vector2(10f, 13f) };
            for (int i = 0; i < baseSlots.Length; i++)
            {
                var sl = new GameObject("Slot_" + i); sl.transform.SetParent(slotRoot.transform, false);
                sl.transform.position = new Vector3(baseSlots[i].x, 0f, baseSlots[i].y) * MapScale;
                sl.AddComponent<ItemSlotMarker>();
            }

            ArenaConfig arena = Asset<ArenaConfig>(Root + "/Config/ArenaConfig.asset");
            ArenaBaker.Bake(map, arena);

            arenaPrefab = PrefabUtility.SaveAsPrefabAsset(map, Root + "/Prefabs/Arena/Arena_Laser.prefab");
            Object.DestroyImmediate(map);
            return arena;
        }

        static GameObject Mesh(Transform parent, string fbxPath, string childName, string name, Vector3 position, float scale, string materialPath)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            Transform part = fbx.GetComponentsInChildren<Transform>(true).First(t => t.name == childName);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position; go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var mats = new Material[part.GetComponent<MeshRenderer>().sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
            return go;
        }

        // ------------------------------------------------------------------ network

        /// <summary>The two things Mirror spawns: one NetTank per player or bot, and the single NetMatch. Neither draws anything.</summary>
        static void BuildNetPrefabs()
        {
            var tank = new GameObject("NetTank");
            tank.AddComponent<NetworkIdentity>(); tank.AddComponent<NetTank>();
            PrefabUtility.SaveAsPrefabAsset(tank, Root + "/Prefabs/Net/NetTank.prefab");
            Object.DestroyImmediate(tank);

            var match = new GameObject("NetMatch");
            match.AddComponent<NetworkIdentity>(); match.AddComponent<NetMatch>();
            PrefabUtility.SaveAsPrefabAsset(match, Root + "/Prefabs/Net/NetMatch.prefab");
            Object.DestroyImmediate(match);
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene(TankConfig tank, WeaponConfig weapons, MatchConfig match, ArenaConfig arena, PlayerPalette palette, FxConfig fx, ItemConfig items, CoreConfig cores, ItemSlotView itemSlot, TankView tankPrefab, GameObject arenaPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // replacing the scene unloads unreferenced assets, so references held in variables can go stale: take fresh ones from disk
            tank = AssetDatabase.LoadAssetAtPath<TankConfig>(Root + "/Config/TankConfig.asset");
            weapons = AssetDatabase.LoadAssetAtPath<WeaponConfig>(Root + "/Config/WeaponConfig.asset");
            match = AssetDatabase.LoadAssetAtPath<MatchConfig>(Root + "/Config/MatchConfig.asset");
            arena = AssetDatabase.LoadAssetAtPath<ArenaConfig>(Root + "/Config/ArenaConfig.asset");
            palette = AssetDatabase.LoadAssetAtPath<PlayerPalette>(Root + "/Config/PlayerPalette.asset");
            fx = AssetDatabase.LoadAssetAtPath<FxConfig>(Root + "/Config/FxConfig.asset");
            items = AssetDatabase.LoadAssetAtPath<ItemConfig>(Root + "/Config/ItemConfig.asset");
            cores = AssetDatabase.LoadAssetAtPath<CoreConfig>(Root + "/Config/CoreConfig.asset");
            itemSlot = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Items/ItemSlot.prefab").GetComponent<ItemSlotView>();
            tankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Tank/TankView.prefab").GetComponent<TankView>();
            arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Arena/Arena_Laser.prefab");

            // lighting as in the pack's own arenas: a warm sun and a bright, flat, cool ambient
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f; light.color = new Color(1f, 0.8235294f, 0.6078432f); light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45f, -33f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6196079f, 0.7960784f, 1f);
            BuildLook();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
            cam.fieldOfView = CameraFov; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.03f, 0.03f);
            camGo.AddComponent<AudioListener>();
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;      // the focus object moves in LateUpdate, before the brain
            camGo.transform.position = CameraStart;

            // what the camera follows, and the Cinemachine camera that follows it from a fixed top-down angle
            var focusGo = new GameObject("CameraFocus"); var focus = focusGo.AddComponent<CameraFocus>();
            var vcamGo = new GameObject("Follow Camera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = CameraFov; vcam.Lens.NearClipPlane = 0.3f; vcam.Lens.FarClipPlane = 600f;
            vcam.Follow = focusGo.transform;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = CameraStart;       // the original close view; CameraFocus only changes how far back along it the camera stands
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = Vector3.zero;          // the focus object is already smoothed
            vcamGo.transform.rotation = Quaternion.LookRotation(-follow.FollowOffset.normalized);     // no aim component: the angle stays as set
            vcamGo.AddComponent<CinemachineImpulseListener>();                                        // receives the shake

            var focusSo = new SerializedObject(focus); focusSo.FindProperty("follow").objectReferenceValue = follow; focusSo.ApplyModifiedPropertiesWithoutUndo();

            var shakerGo = new GameObject("CameraShaker");
            var impulse = shakerGo.AddComponent<CinemachineImpulseSource>();
            var shaker = shakerGo.AddComponent<CinemachineShaker>();
            var shakerSo = new SerializedObject(shaker); shakerSo.FindProperty("source").objectReferenceValue = impulse; shakerSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.InstantiatePrefab(arenaPrefab);

            var rootGo = new GameObject("GameRoot");
            rootGo.AddComponent<ContainerScope>();
            var installer = rootGo.AddComponent<GameInstaller>();
            var loop = rootGo.AddComponent<GameLoop>();
            var fxService = rootGo.AddComponent<FxService>();
            var views = rootGo.AddComponent<TankViewSystem>();
            var projectileViews = rootGo.AddComponent<ProjectileViewSystem>();
            var itemViews = rootGo.AddComponent<ItemViewSystem>();
            rootGo.AddComponent<NetInstaller>();                  // after GameInstaller on purpose: its bindings replace the offline ones
            rootGo.AddComponent<MusicPlayer>();
            var bootstrap = rootGo.AddComponent<GameBootstrap>();
            var bootSo = new SerializedObject(bootstrap); bootSo.FindProperty("autoStartOffline").boolValue = false; bootSo.ApplyModifiedPropertiesWithoutUndo();

            var netGo = new GameObject("Network");
            var manager = netGo.AddComponent<GameNetworkManager>();
            var kcp = netGo.AddComponent<kcp2k.KcpTransport>();
            manager.transport = kcp;
            var discovery = netGo.AddComponent<RoomDiscovery>();
            discovery.transport = kcp; discovery.secretHandshake = 0x54414E4B41524E41;      // the same in every build, so rooms of this game find each other and nothing else
            manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Net/NetTank.prefab");
            GameObject netMatchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Net/NetMatch.prefab");
            manager.spawnPrefabs.Clear(); manager.spawnPrefabs.Add(netMatchPrefab);
            manager.maxConnections = 8; manager.sendRate = 60; manager.autoCreatePlayer = true;
            var mgrSo = new SerializedObject(manager); mgrSo.FindProperty("matchPrefab").objectReferenceValue = netMatchPrefab; mgrSo.ApplyModifiedPropertiesWithoutUndo();

            var menu = rootGo.AddComponent<NetMenuController>();
            var menuSo = new SerializedObject(menu); menuSo.FindProperty("bootstrap").objectReferenceValue = bootstrap; menuSo.FindProperty("manager").objectReferenceValue = manager; menuSo.FindProperty("defaultAddress").stringValue = HostAddress; menuSo.FindProperty("discovery").objectReferenceValue = discovery; menuSo.ApplyModifiedPropertiesWithoutUndo();
            UiTheme uiTheme = UiBuilder.BuildCanvas(menu, out GameObject hud);

            var so = new SerializedObject(installer);
            so.FindProperty("tank").objectReferenceValue = tank; so.FindProperty("weapons").objectReferenceValue = weapons; so.FindProperty("match").objectReferenceValue = match;
            so.FindProperty("arena").objectReferenceValue = arena; so.FindProperty("palette").objectReferenceValue = palette; so.FindProperty("fx").objectReferenceValue = fx;
            so.FindProperty("loop").objectReferenceValue = loop; so.FindProperty("fxService").objectReferenceValue = fxService;
            so.FindProperty("tankViews").objectReferenceValue = views; so.FindProperty("projectileViews").objectReferenceValue = projectileViews; so.FindProperty("shaker").objectReferenceValue = shaker;
            so.FindProperty("items").objectReferenceValue = items; so.FindProperty("cores").objectReferenceValue = cores;
            so.FindProperty("itemViews").objectReferenceValue = itemViews; so.FindProperty("theme").objectReferenceValue = uiTheme;
            so.FindProperty("viewPrefabs").FindPropertyRelative("itemSlot").objectReferenceValue = itemSlot;
            so.FindProperty("viewPrefabs").FindPropertyRelative("tank").objectReferenceValue = tankPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
        }

        /// <summary>The picture's finish: a little contrast and colour everywhere, and bloom on desktops only. No tone mapping, which would darken the pack's warm look.</summary>
        static void BuildLook()
        {
            VolumeProfile Profile(string name, System.Action<VolumeProfile> fill)
            {
                string path = Root + "/Config/" + name + ".asset";
                AssetDatabase.DeleteAsset(path);
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                fill(profile);
                EditorUtility.SetDirty(profile);
                return profile;
            }
            T Add<T>(VolumeProfile profile) where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            VolumeProfile baseProfile = Profile("LookVolume", p =>
            {
                var grade = Add<UnityEngine.Rendering.Universal.ColorAdjustments>(p); grade.postExposure.Override(0.7f); grade.contrast.Override(6f); grade.saturation.Override(22f);
            });
            VolumeProfile bloomProfile = Profile("BloomVolume", p =>
            {
                var bloom = Add<UnityEngine.Rendering.Universal.Bloom>(p); bloom.threshold.Override(1f); bloom.intensity.Override(0.55f); bloom.scatter.Override(0.6f);
            });

            var look = new GameObject("Look"); var lookVolume = look.AddComponent<Volume>(); lookVolume.isGlobal = true; lookVolume.sharedProfile = baseProfile;
            var bloomGo = new GameObject("Bloom (desktop)"); bloomGo.transform.SetParent(look.transform, false);
            var bloomVolume = bloomGo.AddComponent<Volume>(); bloomVolume.isGlobal = true; bloomVolume.sharedProfile = bloomProfile;
            bloomGo.AddComponent<DisableOnMobile>();
        }

        // ------------------------------------------------------------------ helpers

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); }
            return a;
        }

        /// <summary>Reflex reads its settings from Resources at startup; the class is internal to the package, so it is created by name.</summary>
        static void EnsureReflexSettings()
        {
            const string path = "Assets/Resources/ReflexSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) != null) return;
            System.Type type = System.Type.GetType("Reflex.Configuration.ReflexSettings, Reflex");
            if (type == null) { Debug.LogError("Reflex settings type not found; is the Reflex package installed?"); return; }
            EnsureFolder("Assets/Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(type), path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
