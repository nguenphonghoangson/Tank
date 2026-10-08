using System.IO;
using System.Linq;
using TMPro;
using Tank.Gameplay.UI;
using Tank.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Tank.Editor
{
    /// <summary>
    /// Builds the interface: the theme asset (generated sprites + pack icons + fonts) and the HUD canvas with every presenter wired.
    /// Layout is authored at 1920x1080 and scaled by the CanvasScaler; the safe area keeps it out of notches.
    /// </summary>
    public static class UiBuilder
    {
        const string Dir = "Assets/Game/Content/UI";

        static UiTheme s_Theme;
        static TMP_FontAsset s_Body, s_Heading;

        // ------------------------------------------------------------------ theme

        public static UiTheme BuildTheme()
        {
            Directory.CreateDirectory(Dir);
            string themePath = Dir + "/UiTheme.asset";
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(themePath);
            if (theme == null) { theme = ScriptableObject.CreateInstance<UiTheme>(); AssetDatabase.CreateAsset(theme, themePath); }

            theme.square = Generated("square", 8, 0, (x, y, n) => 1f);
            theme.disc = Generated("disc", 64, 0, (x, y, n) => Mathf.Clamp01((0.5f - Mathf.Sqrt(x * x + y * y)) * n * 0.5f + 0.5f));
            theme.ring = Generated("ring", 128, 0, (x, y, n) => { float r = Mathf.Sqrt(x * x + y * y); return Mathf.Clamp01((0.5f - r) * n * 0.5f + 0.5f) * Mathf.Clamp01((r - 0.46f) * n * 0.5f + 0.5f); });
            theme.rounded = Generated("rounded", 64, 20, (x, y, n) =>
            {
                float hx = Mathf.Abs(x) - (0.5f - 0.32f), hy = Mathf.Abs(y) - (0.5f - 0.32f);
                float d = Mathf.Sqrt(Mathf.Max(hx, 0f) * Mathf.Max(hx, 0f) + Mathf.Max(hy, 0f) * Mathf.Max(hy, 0f)) - 0.32f;
                return Mathf.Clamp01(-d * n * 0.5f + 0.5f);
            });
            theme.vignette = Generated("vignette", 128, 0, (x, y, n) => { float r = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) * 2f; return Mathf.Pow(Mathf.Clamp01((r - 0.55f) / 0.45f), 1.6f); });

            Sprite Pack(string name) { return AssetDatabase.LoadAllAssetsAtPath("Assets/Textures/UI.png").OfType<Sprite>().FirstOrDefault(s => s.name == name); }
            theme.panelSmall = Pack("UI_4") ?? theme.rounded; theme.panelLarge = Pack("UI_3") ?? theme.rounded;
            theme.iconHealth = Pack("UI_8"); theme.iconAmmo = Pack("UI_6"); theme.iconSpeed = Pack("UI_7"); theme.iconDamage = Pack("UI_13");
            theme.iconDash = Pack("UI_14"); theme.iconShield = Pack("UI_9"); theme.iconKill = Pack("UI_13");
            theme.crown = AssetDatabase.LoadAllAssetsAtPath("Assets/Textures/Crown.png").OfType<Sprite>().FirstOrDefault();
            theme.body = Font("RobotoCondensed-Bold"); theme.heading = Font("Roboto-Black");
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        static TMP_FontAsset Font(string hint)
        {
            foreach (string g in AssetDatabase.FindAssets("t:TMP_FontAsset " + hint))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g));
            return TMP_Settings.defaultFontAsset;
        }

        delegate float Shape(float x, float y, float pixelsPerUnit);

        /// <summary>A white sprite whose alpha follows shape(x, y) for x, y in [-0.5, 0.5]; `border` makes it nine-sliceable.</summary>
        static Sprite Generated(string name, int size, int border, Shape shape)
        {
            string path = Dir + "/" + name + ".png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, shape((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f, size)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false; importer.spriteBorder = new Vector4(border, border, border, border); importer.spritePixelsPerUnit = 100f;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------ small construction helpers

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Corner(RectTransform rt, Vector2 corner, Vector2 pos, Vector2 size) { return Place(rt, corner, corner, corner, pos, size); }
        static RectTransform Stretch(RectTransform rt) { return Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero); }
        static RectTransform Center(RectTransform rt, Vector2 pos, Vector2 size) { return Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size); }

        static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced = true, bool raycast = false)
        {
            RectTransform rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
            img.type = sprite != null && sliced && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            return img;
        }

        static TMP_Text Txt(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool heading = false)
        {
            RectTransform rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            t.font = heading ? s_Heading : s_Body; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static Button ButtonOn(Image image, Color normal) { var b = image.gameObject.AddComponent<Button>(); image.raycastTarget = true; b.targetGraphic = image; var c = b.colors; c.normalColor = normal; c.highlightedColor = Color.Lerp(normal, Color.white, 0.25f); c.pressedColor = Color.Lerp(normal, Color.black, 0.25f); c.colorMultiplier = 1f; b.colors = c; return b; }

        static void Set(Object target, string field, Object value) { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }

        static void SetMany(Object target, params (string field, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values) { var p = so.FindProperty(field); if (p == null) Debug.LogWarning(target.GetType().Name + " has no field " + field); else p.objectReferenceValue = value; }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Image Panel(Transform parent, string name, Color tint) { return Img(parent, name, s_Theme.rounded, tint); }

        static (RectTransform back, RectTransform fill, Image fillImage) Bar(Transform parent, string name, Vector2 size, Color back, Color fill)
        {
            Image bg = Panel(parent, name, back);
            Place(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, size);
            Image f = Img(bg.transform, "Fill", s_Theme.rounded, fill);
            Place(f.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            return (bg.rectTransform, f.rectTransform, f);
        }

        // ------------------------------------------------------------------ prefabs for repeated rows

        static RankingRowView BuildRowPrefab(string name, float width)
        {
            var root = Node(name, null);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            Image bg = Img(root, "Background", s_Theme.rounded, Color.clear); Stretch(bg.rectTransform);
            Image pip = Img(root, "Pip", s_Theme.disc, Color.white); Place(pip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(14f, 14f));
            TMP_Text place = Txt(root, "Place", "1", 22f, s_Theme.textMuted, TextAlignmentOptions.Center, true); Place(place.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(28f, 0f));
            TMP_Text pname = Txt(root, "Name", "Player", 24f, s_Theme.text); Place(pname.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(58f, 0f), new Vector2(-58f - 4 * 56f - 64f, 0f));
            float x = width - 8f;
            TMP_Text kda = Txt(root, "KDA", "0.00", 24f, s_Theme.accent, TextAlignmentOptions.Right, true); Place(kda.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(64f, 0f));
            TMP_Text a = Txt(root, "A", "0", 22f, s_Theme.textMuted, TextAlignmentOptions.Right); Place(a.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-72f, 0f), new Vector2(48f, 0f));
            TMP_Text d = Txt(root, "D", "0", 22f, s_Theme.textMuted, TextAlignmentOptions.Right); Place(d.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-124f, 0f), new Vector2(48f, 0f));
            TMP_Text k = Txt(root, "K", "0", 22f, s_Theme.text, TextAlignmentOptions.Right); Place(k.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-176f, 0f), new Vector2(48f, 0f));
            var view = root.gameObject.AddComponent<RankingRowView>();
            SetMany(view, ("place", place), ("playerName", pname), ("kills", k), ("deaths", d), ("assists", a), ("kda", kda), ("colorPip", pip), ("background", bg));
            string path = Dir + "/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
            Object.DestroyImmediate(root.gameObject);
            return prefab.GetComponent<RankingRowView>();
        }

        static KillFeedEntryView BuildKillFeedEntry()
        {
            var root = Node("KillFeedEntry", null);
            root.sizeDelta = new Vector2(360f, 32f);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            Image bg = Img(root, "Background", s_Theme.rounded, new Color(0f, 0f, 0f, 0.45f)); Stretch(bg.rectTransform); bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight; row.spacing = 8f; row.padding = new RectOffset(10, 10, 2, 2); row.childForceExpandWidth = false; row.childControlWidth = true; row.childControlHeight = true;
            var killerBlock = Node("KillerBlock", root);
            var kl = killerBlock.gameObject.AddComponent<HorizontalLayoutGroup>(); kl.spacing = 8f; kl.childForceExpandWidth = false; kl.childControlWidth = true; kl.childControlHeight = true;
            TMP_Text killer = Txt(killerBlock, "Killer", "Killer", 22f, s_Theme.text, TextAlignmentOptions.Right);
            Image icon = Img(killerBlock, "Icon", s_Theme.iconKill, s_Theme.textMuted, false); icon.gameObject.AddComponent<LayoutElement>().preferredWidth = 22f;
            TMP_Text victim = Txt(root, "Victim", "Victim", 22f, s_Theme.text, TextAlignmentOptions.Right);
            foreach (var t in new[] { killer, victim }) t.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var view = root.gameObject.AddComponent<KillFeedEntryView>();
            SetMany(view, ("killer", killer), ("victim", victim), ("killerBlock", killerBlock.gameObject), ("highlight", bg), ("group", group));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Dir + "/KillFeedEntry.prefab");
            Object.DestroyImmediate(root.gameObject);
            return prefab.GetComponent<KillFeedEntryView>();
        }

        // ------------------------------------------------------------------ the HUD

        /// <summary>Creates the HUD canvas, the menu, the EventSystem, and returns the theme so the installer can bind it.</summary>
        public static UiTheme BuildCanvas(NetMenuController menu, out GameObject canvasGo)
        {
            s_Theme = BuildTheme();
            s_Body = s_Theme.body; s_Heading = s_Theme.heading;
            Color ink = new Color(0.07f, 0.08f, 0.11f, 0.78f);

            RankingRowView rowPrefab = BuildRowPrefab("RankingRow", 440f);
            RankingRowView resultRowPrefab = BuildRowPrefab("ResultRow", 700f);
            KillFeedEntryView feedPrefab = BuildKillFeedEntry();

            canvasGo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0.5f;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            Image flash = Img(canvasGo.transform, "DamageFlash", s_Theme.vignette, new Color(1f, 0.1f, 0.05f, 0f)); Stretch(flash.rectTransform); flash.type = Image.Type.Simple;
            canvasGo.AddComponent<DamageFlashPresenter>();
            Set(canvasGo.GetComponent<DamageFlashPresenter>(), "vignette", flash);

            RectTransform safe = Node("SafeArea", canvasGo.transform); Stretch(safe); safe.gameObject.AddComponent<SafeAreaFitter>();

            BuildRanking(safe, rowPrefab, ink);
            BuildOffscreenMarkers(safe);
            BuildClock(safe, ink);
            BuildTopRight(safe, ink, feedPrefab, menu);
            BuildPlayerCard(safe, ink);
            BuildCoreOffer(safe, ink);
            BuildTouch(safe, canvas);
            BuildResults(safe, ink, resultRowPrefab);
            BuildLobby(canvasGo.transform, ink, menu);
            BuildMenu(canvasGo.transform, menu, safe);
            return s_Theme;
        }

        static void BuildRanking(RectTransform parent, RankingRowView rowPrefab, Color ink)
        {
            Image panel = Panel(parent, "Ranking", ink);
            Corner(panel.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(440f, 56f + 6 * 34f));
            TMP_Text head = Txt(panel.transform, "Header", "RANKING", 20f, s_Theme.textMuted, TextAlignmentOptions.Left, true); Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(0f, 24f));
            TMP_Text cols = Txt(panel.transform, "Columns", "K      D      A      KDA", 18f, s_Theme.textMuted, TextAlignmentOptions.Right); Place(cols.rectTransform, new Vector2(0.4f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -9f), new Vector2(0f, 24f));
            RectTransform rows = Node("Rows", panel.transform); Place(rows, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(-16f, -44f));
            var v = rows.gameObject.AddComponent<VerticalLayoutGroup>(); v.childForceExpandHeight = false; v.childControlHeight = true; v.childControlWidth = true; v.spacing = 2f;
            var p = panel.gameObject.AddComponent<RankingPanelPresenter>();
            Set(p, "rowsRoot", rows); Set(p, "rowPrefab", rowPrefab);
        }

        static void BuildOffscreenMarkers(RectTransform parent)
        {
            var root = Node("OffscreenMarkers", parent); Stretch(root);
            Image dot = Img(root, "Dot", s_Theme.disc, Color.white); dot.rectTransform.sizeDelta = new Vector2(26f, 26f); dot.gameObject.SetActive(false);
            Image outline = Img(dot.transform, "Outline", s_Theme.ring, new Color(0f, 0f, 0f, 0.7f)); Stretch(outline.rectTransform); outline.rectTransform.sizeDelta = new Vector2(8f, 8f);
            var presenter = root.gameObject.AddComponent<OffscreenMarkersPresenter>();
            SetMany(presenter, ("root", root), ("dotPrefab", dot));
        }

        static void BuildClock(RectTransform parent, Color ink)
        {
            Image panel = Panel(parent, "Clock", ink);
            Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(190f, 84f));
            TMP_Text timer = Txt(panel.transform, "Timer", "3:00", 46f, s_Theme.text, TextAlignmentOptions.Center, true); Place(timer.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            TMP_Text phase = Txt(panel.transform, "Phase", "WARM-UP", 18f, s_Theme.textMuted, TextAlignmentOptions.Center, true); Place(phase.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.32f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var bannerRoot = Node("Banner", parent); Center(bannerRoot, new Vector2(0f, 120f), new Vector2(900f, 220f));
            var group = bannerRoot.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false;
            TMP_Text banner = Txt(bannerRoot, "Text", string.Empty, 150f, s_Theme.accent, TextAlignmentOptions.Center, true); Stretch(banner.rectTransform);
            var presenter = panel.gameObject.AddComponent<MatchClockPresenter>();
            SetMany(presenter, ("timer", timer), ("phase", phase), ("banner", banner), ("bannerGroup", group));
        }

        static void BuildTopRight(RectTransform parent, Color ink, KillFeedEntryView feedPrefab, NetMenuController menu)
        {
            // kill feed, below the session bar
            RectTransform feed = Node("KillFeed", parent); Corner(feed, new Vector2(1f, 1f), new Vector2(-24f, -84f), new Vector2(360f, 160f));
            feed.pivot = new Vector2(1f, 1f);
            var layout = feed.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childAlignment = TextAnchor.UpperRight; layout.spacing = 4f; layout.childForceExpandHeight = false; layout.childForceExpandWidth = false; layout.childControlWidth = false; layout.childControlHeight = true;
            var fp = feed.gameObject.AddComponent<KillFeedPresenter>();
            SetMany(fp, ("root", feed), ("entryPrefab", feedPrefab));
        }

        static void BuildPlayerCard(RectTransform parent, Color ink)
        {
            Image panel = Panel(parent, "PlayerCard", ink);
            Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-210f, 22f), new Vector2(560f, 150f));
            var group = panel.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;

            Image pip = Img(panel.transform, "Pip", s_Theme.disc, Color.white); Corner(pip.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -20f), new Vector2(18f, 18f));
            TMP_Text pname = Txt(panel.transform, "Name", "Player", 28f, s_Theme.text, TextAlignmentOptions.Left, true); Corner(pname.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -12f), new Vector2(300f, 34f));

            // health bar with the shield laid over it
            var hp = Bar(panel.transform, "Health", new Vector2(520f, 26f), new Color(0f, 0f, 0f, 0.5f), s_Theme.good); Corner(hp.back, new Vector2(0f, 1f), new Vector2(20f, -54f), new Vector2(520f, 26f));
            Image shieldImg = Img(hp.back, "Shield", s_Theme.rounded, new Color(s_Theme.shield.r, s_Theme.shield.g, s_Theme.shield.b, 0.75f));
            Place(shieldImg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.38f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            TMP_Text hpText = Txt(hp.back, "Text", "100", 20f, s_Theme.text, TextAlignmentOptions.Center, true); Stretch(hpText.rectTransform);

            TMP_Text weapon = Txt(panel.transform, "Weapon", "Cannon", 24f, s_Theme.text, TextAlignmentOptions.Left, true); Corner(weapon.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 44f), new Vector2(250f, 32f));
            TMP_Text ammo = Txt(panel.transform, "Ammo", string.Empty, 24f, s_Theme.accent, TextAlignmentOptions.Right, true); Corner(ammo.rectTransform, new Vector2(1f, 0f), new Vector2(-20f, 44f), new Vector2(120f, 32f));

            Image dashIcon = Img(panel.transform, "DashIcon", s_Theme.iconDash, s_Theme.textMuted, false); Corner(dashIcon.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 10f), new Vector2(26f, 26f));
            var dash = Bar(panel.transform, "Dash", new Vector2(250f, 12f), new Color(0f, 0f, 0f, 0.5f), s_Theme.accent); Corner(dash.back, new Vector2(0f, 0f), new Vector2(54f, 17f), new Vector2(250f, 12f));
            TMP_Text dashText = Txt(panel.transform, "DashText", "DASH", 18f, s_Theme.textMuted, TextAlignmentOptions.Left, true); Corner(dashText.rectTransform, new Vector2(0f, 0f), new Vector2(314f, 8f), new Vector2(80f, 26f));

            // buff chips, right of the card
            RectTransform chips = Node("Chips", panel.transform); Corner(chips, new Vector2(1f, 0f), new Vector2(-16f, 8f), new Vector2(190f, 30f));
            var cl = chips.gameObject.AddComponent<HorizontalLayoutGroup>(); cl.childAlignment = TextAnchor.MiddleRight; cl.spacing = 6f; cl.childForceExpandWidth = false;
            (GameObject go, TMP_Text time) Chip(string name, Sprite icon, Color color)
            {
                Image c = Panel(chips, name, new Color(color.r, color.g, color.b, 0.35f));
                c.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;
                Image i = Img(c.transform, "Icon", icon, color, false); Corner(i.rectTransform, new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(20f, 20f));
                TMP_Text t = Txt(c.transform, "Time", "5", 18f, s_Theme.text, TextAlignmentOptions.Right, true); Place(t.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-4f, 0f), new Vector2(-28f, 0f));
                return (c.gameObject, t);
            }
            var shield = Chip("ShieldChip", s_Theme.iconShield, s_Theme.shield);
            var speed = Chip("SpeedChip", s_Theme.iconSpeed, s_Theme.accent);
            var damage = Chip("DamageChip", s_Theme.iconDamage, s_Theme.bad);

            // cores: a column to the right of the card
            RectTransform cores = Node("Cores", parent); Place(cores, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(360f, 22f), new Vector2(300f, 150f));
            var col = cores.gameObject.AddComponent<VerticalLayoutGroup>(); col.childAlignment = TextAnchor.LowerLeft; col.spacing = 2f; col.childForceExpandHeight = false; col.childControlHeight = true; col.childControlWidth = true;
            TMP_Text coreEntry = Txt(null, "CoreEntry", "Core", 22f, s_Theme.text, TextAlignmentOptions.Left, true); coreEntry.transform.SetParent(cores, false); coreEntry.gameObject.SetActive(false);
            coreEntry.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            var presenter = panel.gameObject.AddComponent<PlayerCardPresenter>();
            SetMany(presenter, ("nameText", pname), ("colorPip", pip), ("healthFill", hp.fill), ("healthFillImage", hp.fillImage), ("shieldFill", shieldImg.rectTransform), ("healthText", hpText),
                ("weaponText", weapon), ("ammoText", ammo), ("dashFill", dash.fill), ("dashText", dashText),
                ("shieldChip", shield.go), ("speedChip", speed.go), ("damageChip", damage.go), ("shieldTime", shield.time), ("speedTime", speed.time), ("damageTime", damage.time),
                ("coreRoot", cores), ("coreEntryPrefab", coreEntry), ("group", group));
        }

        static void BuildCoreOffer(RectTransform parent, Color ink)
        {
            var root = Node("CoreOffer", parent); Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            Image dim = Img(root, "Dim", s_Theme.square, new Color(0f, 0f, 0f, 0.35f)); Stretch(dim.rectTransform);
            RectTransform panel = Node("Panel", root); Center(panel, new Vector2(0f, 40f), new Vector2(1040f, 380f));
            TMP_Text title = Txt(panel, "Title", "CHOOSE A CORE", 44f, s_Theme.accent, TextAlignmentOptions.Center, true); Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 56f));
            var cards = new CoreCardView[3];
            for (int i = 0; i < 3; i++)
            {
                Image card = Panel(panel, "Card" + (i + 1), ink);
                Place(card.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(i * 350f, 44f), new Vector2(340f, 270f));
                Button button = ButtonOn(card, ink);
                Image accent = Img(card.transform, "Accent", s_Theme.rounded, Color.white); Place(accent.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 10f));
                TMP_Text number = Txt(card.transform, "Number", "1", 34f, s_Theme.textMuted, TextAlignmentOptions.Center, true); Corner(number.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -24f), new Vector2(40f, 44f));
                TMP_Text ctitle = Txt(card.transform, "Title", "Core", 34f, Color.white, TextAlignmentOptions.Center, true); Place(ctitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(-60f, 50f));
                TMP_Text desc = Txt(card.transform, "Description", "What it does", 26f, s_Theme.text, TextAlignmentOptions.Top); desc.textWrappingMode = TextWrappingModes.Normal; desc.overflowMode = TextOverflowModes.Overflow;
                Place(desc.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(-48f, -150f));
                var view = card.gameObject.AddComponent<CoreCardView>();
                SetMany(view, ("number", number), ("title", ctitle), ("description", desc), ("accent", accent), ("button", button));
                cards[i] = view;
            }
            Image timerBack = Panel(panel, "TimerBack", new Color(0f, 0f, 0f, 0.5f)); Place(timerBack.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(-10f, 10f));
            Image timerFill = Img(timerBack.transform, "Fill", s_Theme.rounded, s_Theme.accent); Place(timerFill.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            TMP_Text hint = Txt(panel, "Hint", string.Empty, 22f, s_Theme.textMuted, TextAlignmentOptions.Center); Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 26f));

            var presenter = root.gameObject.AddComponent<CoreOfferPresenter>();
            SetMany(presenter, ("group", group), ("panel", panel), ("timerFill", timerFill.rectTransform), ("hint", hint));
            var so = new SerializedObject(presenter); var arr = so.FindProperty("cards"); arr.arraySize = 3;
            for (int i = 0; i < 3; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildTouch(RectTransform parent, Canvas canvas)
        {
            var root = Node("TouchControls", parent); Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            Color faint = new Color(1f, 1f, 1f, 0.16f);

            Image split = Img(root, "SplitLine", s_Theme.square, new Color(1f, 1f, 1f, 0.07f));
            Place(split.rectTransform, new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 0f)); split.gameObject.SetActive(false);      // the two rings already mark the halves

            (RectTransform hint, RectTransform stick, RectTransform knob, Image knobImage) Zone(string name, string label)
            {
                Image hint = Img(root, name + "Hint", s_Theme.ring, faint); hint.rectTransform.sizeDelta = new Vector2(240f, 240f);
                TMP_Text l = Txt(hint.transform, "Label", label, 28f, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.Center, true); Stretch(l.rectTransform);
                Image stick = Img(root, name + "Base", s_Theme.ring, new Color(1f, 1f, 1f, 0.35f)); stick.rectTransform.sizeDelta = new Vector2(240f, 240f);
                Image knob = Img(root, name + "Knob", s_Theme.disc, new Color(1f, 1f, 1f, 0.55f)); knob.rectTransform.sizeDelta = new Vector2(100f, 100f);
                foreach (var r in new[] { hint.rectTransform, stick.rectTransform, knob.rectTransform }) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f); }
                stick.gameObject.SetActive(false); knob.gameObject.SetActive(false);
                return (hint.rectTransform, stick.rectTransform, knob.rectTransform, knob);
            }
            var move = Zone("Move", "MOVE");
            var aim = Zone("Aim", "AIM · FIRE");

            Image dashBack = Img(root, "DashButton", s_Theme.disc, new Color(1f, 1f, 1f, 0.32f)); dashBack.rectTransform.anchorMin = dashBack.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); dashBack.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Image dashFill = Img(dashBack.transform, "Cooldown", s_Theme.disc, new Color(0f, 0f, 0f, 0.55f)); Stretch(dashFill.rectTransform);
            dashFill.type = Image.Type.Filled; dashFill.fillMethod = Image.FillMethod.Radial360; dashFill.fillOrigin = (int)Image.Origin360.Top; dashFill.fillClockwise = false; dashFill.fillAmount = 0f;
            TMP_Text dashLabel = Txt(dashBack.transform, "Label", "DASH", 26f, Color.white, TextAlignmentOptions.Center, true); Stretch(dashLabel.rectTransform);

            var p = root.gameObject.AddComponent<TouchControlsPresenter>();
            SetMany(p, ("group", group), ("moveHint", move.hint), ("moveBase", move.stick), ("moveKnob", move.knob), ("aimHint", aim.hint), ("aimBase", aim.stick), ("aimKnob", aim.knob), ("aimKnobImage", aim.knobImage),
                ("dashButton", dashBack.rectTransform), ("dashFill", dashFill), ("dashBack", dashBack), ("dashLabel", dashLabel), ("splitLine", split.rectTransform), ("canvas", canvas));
        }

        static void BuildResults(RectTransform parent, Color ink, RankingRowView rowPrefab)
        {
            var root = Node("Results", parent); Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            Image dim = Img(root, "Dim", s_Theme.square, new Color(0f, 0f, 0f, 0.6f)); Stretch(dim.rectTransform);
            Image panel = Panel(root, "Panel", ink); Center(panel.rectTransform, Vector2.zero, new Vector2(760f, 640f));
            Image crown = Img(panel.transform, "Crown", s_Theme.crown, Color.white, false); Place(crown.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, -40f), new Vector2(110f, 80f));
            TMP_Text title = Txt(panel.transform, "Title", "YOU WIN!", 72f, s_Theme.accent, TextAlignmentOptions.Center, true); Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(0f, 90f));
            TMP_Text sub = Txt(panel.transform, "Subtitle", string.Empty, 30f, s_Theme.textMuted, TextAlignmentOptions.Center); Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(0f, 40f));
            RectTransform rows = Node("Rows", panel.transform); Place(rows, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(-48f, -270f));
            var v = rows.gameObject.AddComponent<VerticalLayoutGroup>(); v.childForceExpandHeight = false; v.childControlHeight = true; v.childControlWidth = true; v.spacing = 3f;
            TMP_Text footer = Txt(panel.transform, "Footer", string.Empty, 26f, s_Theme.textMuted, TextAlignmentOptions.Center); Place(footer.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(0f, 36f));
            var presenter = root.gameObject.AddComponent<ResultsPresenter>();
            SetMany(presenter, ("group", group), ("title", title), ("subtitle", sub), ("footer", footer), ("crown", crown), ("rowsRoot", rows), ("rowPrefab", rowPrefab));
        }

        static void BuildLobby(Transform parent, Color ink, NetMenuController menu)
        {
            var root = Node("Lobby", parent); Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            Image dim = Img(root, "Dim", s_Theme.square, new Color(0.04f, 0.05f, 0.07f, 1f), true, true); Stretch(dim.rectTransform);
            Image panel = Panel(root, "Panel", new Color(0.1f, 0.12f, 0.16f, 0.95f)); Center(panel.rectTransform, Vector2.zero, new Vector2(640f, 720f));
            TMP_Text title = Txt(panel.transform, "Title", "LOBBY", 60f, s_Theme.accent, TextAlignmentOptions.Center, true); Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(0f, 76f));
            TMP_Text count = Txt(panel.transform, "Count", "1 player", 26f, s_Theme.textMuted, TextAlignmentOptions.Center); Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(0f, 36f));
            RectTransform list = Node("Players", panel.transform); Place(list, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(-80f, -330f));
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>(); v.childForceExpandHeight = false; v.childControlHeight = true; v.childControlWidth = true; v.spacing = 4f;
            TMP_Text rowPrefab = Txt(null, "PlayerRow", "Player", 32f, s_Theme.text, TextAlignmentOptions.Left, true); rowPrefab.transform.SetParent(panel.transform, false); rowPrefab.richText = true;
            rowPrefab.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f; rowPrefab.gameObject.SetActive(false);

            var hostControls = Node("HostControls", panel.transform); Place(hostControls, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(-80f, 170f));
            TMP_Text botsLabel = Txt(hostControls, "BotsLabel", "Bots", 24f, s_Theme.textMuted, TextAlignmentOptions.Left, true); Corner(botsLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(120f, 46f));
            Image box = Img(hostControls, "BotsField", s_Theme.rounded, new Color(0f, 0f, 0f, 0.5f), true, true); Corner(box.rectTransform, new Vector2(0f, 1f), new Vector2(130f, 0f), new Vector2(120f, 46f));
            RectTransform area = Node("TextArea", box.transform); Stretch(area); area.offsetMin = new Vector2(14f, 4f); area.offsetMax = new Vector2(-14f, -4f); area.gameObject.AddComponent<RectMask2D>();
            TMP_Text ph = Txt(area, "Placeholder", "0 - 7", 26f, new Color(1f, 1f, 1f, 0.3f)); Stretch(ph.rectTransform);
            TMP_Text txt = Txt(area, "Text", "0", 26f, s_Theme.text, TextAlignmentOptions.Left); Stretch(txt.rectTransform);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area; field.textComponent = txt; field.placeholder = ph; field.contentType = TMP_InputField.ContentType.IntegerNumber; field.targetGraphic = box; field.characterLimit = 1; field.text = "2";
            Image startImg = Panel(hostControls, "StartButton", new Color(0.2f, 0.5f, 0.25f)); Place(startImg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 70f));
            Button start = ButtonOn(startImg, startImg.color);
            TMP_Text startText = Txt(startImg.transform, "Label", "START MATCH", 34f, Color.white, TextAlignmentOptions.Center, true); Stretch(startText.rectTransform);

            TMP_Text waiting = Txt(panel.transform, "Waiting", "Waiting for the host to start...", 28f, s_Theme.textMuted, TextAlignmentOptions.Center); Place(waiting.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(0f, 40f));

            var presenter = root.gameObject.AddComponent<LobbyPresenter>();
            SetMany(presenter, ("controller", menu), ("group", group), ("listRoot", list), ("rowPrefab", rowPrefab), ("countText", count), ("waitingText", waiting), ("hostControls", hostControls.gameObject), ("botsField", field), ("startButton", start));
        }

        static void BuildRoomList(RectTransform menuRoot, NetMenuController menu)
        {
            Image panel = Panel(menuRoot, "RoomsPanel", new Color(0.1f, 0.12f, 0.16f, 0.95f)); Center(panel.rectTransform, new Vector2(340f, -90f), new Vector2(620f, 640f));
            TMP_Text title = Txt(panel.transform, "Title", "ROOMS ON THIS NETWORK", 26f, s_Theme.textMuted, TextAlignmentOptions.Left, true); Corner(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -24f), new Vector2(540f, 34f));
            RectTransform rows = Node("Rows", panel.transform); Place(rows, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(-80f, -110f));
            var v = rows.gameObject.AddComponent<VerticalLayoutGroup>(); v.childForceExpandHeight = false; v.childControlHeight = true; v.childControlWidth = true; v.spacing = 8f;
            TMP_Text empty = Txt(panel.transform, "Empty", "No rooms found.\nAsk a friend to press HOST A MATCH on the same Wi-Fi,\nor type their address and press JOIN.", 24f, s_Theme.textMuted, TextAlignmentOptions.Center);
            empty.textWrappingMode = TextWrappingModes.Normal; empty.overflowMode = TextOverflowModes.Overflow; Place(empty.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-80f, 160f));

            // one row: a button with the host's name on the left and the headcount on the right
            Image row = Panel(null, "RoomRow", new Color(0.2f, 0.26f, 0.34f)); row.transform.SetParent(panel.transform, false);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            Button button = ButtonOn(row, row.color);
            TMP_Text rowTitle = Txt(row.transform, "Title", "Host's room", 28f, Color.white, TextAlignmentOptions.Left, true); Place(rowTitle.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(0f, 0f));
            TMP_Text rowStatus = Txt(row.transform, "Status", "1/8   LOBBY", 24f, s_Theme.accent, TextAlignmentOptions.Right, true); Place(rowStatus.rectTransform, new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(0f, 0f));
            var rowView = row.gameObject.AddComponent<RoomRowView>();
            SetMany(rowView, ("title", rowTitle), ("status", rowStatus), ("button", button));
            row.gameObject.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(row.gameObject, Dir + "/RoomRow.prefab");
            Object.DestroyImmediate(row.gameObject);

            var presenter = panel.gameObject.AddComponent<RoomListPresenter>();
            var discovery = new SerializedObject(menu).FindProperty("discovery").objectReferenceValue;
            SetMany(presenter, ("controller", menu), ("discovery", discovery), ("rowsRoot", rows), ("rowPrefab", prefab.GetComponent<RoomRowView>()), ("emptyText", empty));
        }

        static void BuildMenu(Transform canvasRoot, NetMenuController menu, RectTransform safe)
        {
            // session bar: top-right, left of the minimap column
            var bar = Node("SessionBar", safe); Corner(bar, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(264f, 48f));
            Image leaveImg = Panel(bar, "Leave", new Color(0.55f, 0.12f, 0.1f, 0.9f)); Place(leaveImg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(110f, 44f));
            Button leave = ButtonOn(leaveImg, leaveImg.color);
            TMP_Text leaveText = Txt(leaveImg.transform, "Label", "LEAVE", 22f, Color.white, TextAlignmentOptions.Center, true); Stretch(leaveText.rectTransform);
            TMP_Text info = Txt(bar, "Info", string.Empty, 22f, s_Theme.textMuted, TextAlignmentOptions.Right, true); Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-62f, 0f), new Vector2(-124f, 0f));

            // main menu
            var menuRoot = Node("MainMenu", canvasRoot); Stretch(menuRoot);
            Image back = Img(menuRoot, "Back", s_Theme.square, new Color(0.04f, 0.05f, 0.07f, 1f), true, true); Stretch(back.rectTransform);
            TMP_Text title = Txt(menuRoot, "Title", "TANK ARENA", 110f, s_Theme.accent, TextAlignmentOptions.Center, true); Center(title.rectTransform, new Vector2(0f, 330f), new Vector2(1200f, 140f));
            Image panel = Panel(menuRoot, "Panel", new Color(0.1f, 0.12f, 0.16f, 0.95f)); Center(panel.rectTransform, new Vector2(-340f, -90f), new Vector2(620f, 640f));

            TMP_InputField Field(string label, string placeholder, float y, TMP_InputField.ContentType type)
            {
                TMP_Text l = Txt(panel.transform, label + "Label", label, 22f, s_Theme.textMuted, TextAlignmentOptions.Left, true); Corner(l.rectTransform, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(160f, 30f));
                Image box = Img(panel.transform, label + "Field", s_Theme.rounded, new Color(0f, 0f, 0f, 0.5f), true, true); Corner(box.rectTransform, new Vector2(0f, 1f), new Vector2(40f, y - 30f), new Vector2(540f, 46f));
                RectTransform area = Node("TextArea", box.transform); Stretch(area); area.offsetMin = new Vector2(14f, 4f); area.offsetMax = new Vector2(-14f, -4f); area.gameObject.AddComponent<RectMask2D>();
                TMP_Text ph = Txt(area, "Placeholder", placeholder, 26f, new Color(1f, 1f, 1f, 0.3f)); Stretch(ph.rectTransform);
                TMP_Text txt = Txt(area, "Text", string.Empty, 26f, s_Theme.text); Stretch(txt.rectTransform);
                var field = box.gameObject.AddComponent<TMP_InputField>();
                field.textViewport = area; field.textComponent = txt; field.placeholder = ph; field.contentType = type; field.targetGraphic = box;
                return field;
            }
            TMP_InputField nameField = Field("Name", "Your name", -24f, TMP_InputField.ContentType.Standard); nameField.characterLimit = 16;
            TMP_InputField botsField = Field("Bots", "0 - 7", -112f, TMP_InputField.ContentType.IntegerNumber); botsField.characterLimit = 1;
            TMP_InputField addressField = Field("Address", "localhost", -200f, TMP_InputField.ContentType.Standard);

            Button MenuButton(string label, float y, Color color)
            {
                Image b = Panel(panel.transform, label + "Button", color); Corner(b.rectTransform, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(540f, 56f));
                TMP_Text t = Txt(b.transform, "Label", label, 28f, Color.white, TextAlignmentOptions.Center, true); Stretch(t.rectTransform);
                return ButtonOn(b, color);
            }
            Button offline = MenuButton("PLAY OFFLINE", -300f, new Color(0.2f, 0.5f, 0.25f));
            Button host = MenuButton("HOST A MATCH", -370f, new Color(0.2f, 0.4f, 0.6f));
            Button join = MenuButton("JOIN", -440f, new Color(0.45f, 0.3f, 0.6f));
            Button hostOnly = MenuButton("HOST ONLY (NO TANK)", -510f, new Color(0.3f, 0.34f, 0.4f));

            BuildRoomList(menuRoot, menu);

            var presenter = menuRoot.gameObject.AddComponent<MainMenuPresenter>();
            SetMany(presenter, ("controller", menu), ("menuRoot", menuRoot.gameObject), ("sessionRoot", bar.gameObject), ("nameField", nameField), ("botsField", botsField), ("addressField", addressField),
                ("offlineButton", offline), ("hostButton", host), ("joinButton", join), ("hostOnlyButton", hostOnly), ("leaveButton", leave), ("sessionText", info));
            // the presenter hides its own root, so it lives on a sibling that stays active
            var holder = new GameObject("MenuPresenter"); holder.transform.SetParent(canvasRoot, false);
            var moved = holder.AddComponent<MainMenuPresenter>();
            EditorUtility.CopySerialized(presenter, moved); Object.DestroyImmediate(presenter);
        }
    }
}
