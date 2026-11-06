using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NiuMa.EditorTools
{
    /// <summary>
    /// 斗地主极简 UI 预制体生成器（美术后续替换）
    /// 用法：Unity -batchmode -quit -projectPath <client> -executeMethod NiuMa.EditorTools.LandlordPrefabBuilder.BuildAll
    /// 产出：Assets/NiuMa/Resources/Prefabs/Game/Landlord/{Hall,Room}.prefab
    /// </summary>
    public static class LandlordPrefabBuilder
    {
        private const string OutDir = "Assets/NiuMa/Resources/Prefabs/Game/Landlord";
        private const string SpritePath = "Assets/NiuMa/Game/Landlord/ui_white.png";

        [MenuItem("NiuMa/Build Landlord Prefabs")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(OutDir);
            EnsureWhiteSprite();

            BuildHall();
            BuildRoom();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LandlordPrefabBuilder] done");
        }

        private static void EnsureWhiteSprite()
        {
            string dir = "Assets/NiuMa/Game/Landlord";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string full = Path.Combine(Application.dataPath, "../" + SpritePath).Replace("\\", "/");
            if (File.Exists(full))
                return;

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        private static Sprite WhiteSprite()
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }

        private static Font UiFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null)
                f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image Panel(string name, Transform parent, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WhiteSprite();
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        private static Text Label(string name, Transform parent, string text, int size, Color color)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiFont();
            t.fontSize = size;
            t.text = text;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }

        private static Button Btn(string name, Transform parent, string text, Color bg, Vector2 pos, Vector2 size)
        {
            var img = Panel(name, parent, bg);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            var lbl = Label("Text", img.transform, text, 26, Color.white);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            return btn;
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void BuildHall()
        {
            var root = Rect("Hall", null);
            root.gameObject.AddComponent<CanvasGroup>();
            Stretch(root);
            root.sizeDelta = new Vector2(1280, 720);

            var bg = Stretch(Panel("Bg", root, new Color(0.05f, 0.22f, 0.16f, 1f)).rectTransform);
            bg.name = "Bg";

            var title = Label("Title", root, "斗地主", 48, new Color(1f, 0.82f, 0.32f, 1f));
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0, -40);
            trt.sizeDelta = new Vector2(600, 60);

            Btn("BtnBack", root, "返回", new Color(0.25f, 0.25f, 0.25f, 1f), new Vector2(-520, 290), new Vector2(140, 56));
            Btn("BtnCreate", root, "创建房间", new Color(0.82f, 0.55f, 0.13f, 1f), new Vector2(300, 290), new Vector2(180, 56));
            Btn("BtnJoin", root, "加入房间", new Color(0.20f, 0.45f, 0.75f, 1f), new Vector2(490, 290), new Vector2(180, 56));
            Btn("BtnRefresh", root, "刷新", new Color(0.25f, 0.45f, 0.35f, 1f), new Vector2(120, 290), new Vector2(140, 56));

            // 房间列表
            var listRt = Rect("RoomList", root);
            listRt.anchorMin = new Vector2(0.5f, 0.5f);
            listRt.anchorMax = new Vector2(0.5f, 0.5f);
            listRt.pivot = new Vector2(0.5f, 0.5f);
            listRt.anchoredPosition = new Vector2(0, -40);
            listRt.sizeDelta = new Vector2(900, 420);
            var scroll = listRt.gameObject.AddComponent<ScrollRect>();
            var viewport = Stretch(Panel("Viewport", listRt, new Color(0, 0, 0, 0.25f)).rectTransform);
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.spacing = 8;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;

            var empty = Label("EmptyTip", root, "暂无房间，点「刷新」试试", 26, new Color(1, 1, 1, 0.6f));
            var ert = empty.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
            ert.anchoredPosition = new Vector2(0, -40);
            ert.sizeDelta = new Vector2(600, 40);

            // 弹窗占位（保持结构一致，默认隐藏）
            BuildDialog("DlgCreate", root, "创建房间");
            BuildDialog("DlgJoin", root, "加入房间");

            PrefabUtility.SaveAsPrefabAsset(root.gameObject, OutDir + "/Hall.prefab");
            Object.DestroyImmediate(root.gameObject);
            Debug.Log("[LandlordPrefabBuilder] Hall.prefab created");
        }

        private static void BuildDialog(string name, Transform parent, string title)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520, 360);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = WhiteSprite();
            bg.color = new Color(0.1f, 0.12f, 0.14f, 0.95f);

            var lbl = Label("Title", rt, title, 32, Color.white);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0, -16);
            lrt.sizeDelta = new Vector2(400, 44);

            Btn("BtnClose", rt, "关闭", new Color(0.4f, 0.2f, 0.2f, 1f), new Vector2(0, -140), new Vector2(160, 52));
            Btn("BtnOk", rt, "确定", new Color(0.82f, 0.55f, 0.13f, 1f), new Vector2(0, 0), new Vector2(180, 56));
            rt.gameObject.SetActive(false);
        }

        private static void BuildRoom()
        {
            var root = Rect("Room", null);
            Stretch(root);
            root.sizeDelta = new Vector2(1280, 720);

            Stretch(Panel("Bg", root, new Color(0.04f, 0.18f, 0.13f, 1f)).rectTransform);

            // 顶部信息
            var info = Label("Info", root, "", 24, Color.white);
            var irt = info.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0, -12);
            irt.sizeDelta = new Vector2(900, 40);

            // 三家手牌数
            Label("Player0", root, "我", 22, Color.white).rectTransform.anchoredPosition = new Vector2(-420, 120);
            Label("Player1", root, "下家", 22, Color.white).rectTransform.anchoredPosition = new Vector2(0, 260);
            Label("Player2", root, "上家", 22, Color.white).rectTransform.anchoredPosition = new Vector2(420, 120);

            // 出牌区
            var table = Rect("Table", root);
            table.anchorMin = table.anchorMax = new Vector2(0.5f, 0.5f);
            table.anchoredPosition = new Vector2(0, 40);
            table.sizeDelta = new Vector2(900, 200);

            // 手牌区
            var hand = Rect("Hand", root);
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0f);
            hand.pivot = new Vector2(0.5f, 0f);
            hand.anchoredPosition = new Vector2(0, 120);
            hand.sizeDelta = new Vector2(1200, 180);
            var hl = hand.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.spacing = 6;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            // 手牌模板（Lua 克隆这个生成每张牌）
            var cardImg = Panel("CardTemplate", root, new Color(1f, 1f, 1f, 1f));
            var crt = cardImg.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0, 240);
            crt.sizeDelta = new Vector2(64, 92);
            var clbl = Label("Text", cardImg.transform, "", 26, new Color(0.1f, 0.1f, 0.1f, 1f));
            var clrt = clbl.rectTransform;
            clrt.anchorMin = Vector2.zero;
            clrt.anchorMax = Vector2.one;
            clrt.offsetMin = Vector2.zero;
            clrt.offsetMax = Vector2.zero;
            cardImg.gameObject.AddComponent<Button>();
            cardImg.gameObject.SetActive(false);

            Btn("BtnPlay", root, "出牌", new Color(0.82f, 0.55f, 0.13f, 1f), new Vector2(-120, -250), new Vector2(180, 64));
            Btn("BtnPass", root, "不出", new Color(0.35f, 0.35f, 0.35f, 1f), new Vector2(80, -250), new Vector2(180, 64));
            Btn("BtnSync", root, "同步", new Color(0.20f, 0.45f, 0.75f, 1f), new Vector2(280, -250), new Vector2(140, 64));

            PrefabUtility.SaveAsPrefabAsset(root.gameObject, OutDir + "/Room.prefab");
            Object.DestroyImmediate(root.gameObject);
            Debug.Log("[LandlordPrefabBuilder] Room.prefab created");
        }
    }
}
