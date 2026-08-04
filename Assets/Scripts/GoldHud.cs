using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DK
{
    /// <summary>
    /// Builds the on-screen gold counter at runtime: Canvas + TextMeshPro.
    /// If the project has no TMP font asset yet (TMP Essential Resources not imported),
    /// it falls back to legacy uGUI text so a fresh clone still shows the counter.
    /// </summary>
    public class GoldHud : MonoBehaviour
    {
        ResourceManager _resources;
        TextMeshProUGUI _tmpLabel;
        Text _legacyLabel;

        const string Hint = "LMB drag: mark for digging   RMB: cancel   WASD / screen edge: pan   Wheel: zoom";

        public void Configure(ResourceManager resources)
        {
            _resources = resources;

            var canvas = BuildCanvas();
            BuildLabel(canvas, out _tmpLabel, out _legacyLabel);
            BuildHint(canvas);

            _resources.GoldChanged += OnGoldChanged;
            OnGoldChanged(_resources.Gold);
        }

        void OnDestroy()
        {
            if (_resources != null) _resources.GoldChanged -= OnGoldChanged;
        }

        void OnGoldChanged(int gold)
        {
            string text = $"Gold: {gold}";
            if (_tmpLabel != null) _tmpLabel.text = text;
            if (_legacyLabel != null) _legacyLabel.text = text;
        }

        RectTransform BuildCanvas()
        {
            var canvasObject = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return (RectTransform)canvasObject.transform;
        }

        void BuildLabel(RectTransform canvas, out TextMeshProUGUI tmp, out Text legacy)
        {
            tmp = null;
            legacy = null;

            var rect = CreateRect(canvas, "Gold Label", new Vector2(24f, -24f), new Vector2(520f, 72f));

            if (TmpFontAvailable())
            {
                tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 46f;
                tmp.color = new Color(1f, 0.85f, 0.35f);
                tmp.alignment = TextAlignmentOptions.TopLeft;
                return;
            }

            legacy = AddLegacyText(rect, 40, new Color(1f, 0.85f, 0.35f));
        }

        void BuildHint(RectTransform canvas)
        {
            var rect = CreateRect(canvas, "Hint Label", new Vector2(24f, -96f), new Vector2(1400f, 48f));
            var color = new Color(0.85f, 0.85f, 0.85f, 0.8f);

            if (TmpFontAvailable())
            {
                var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 24f;
                tmp.color = color;
                tmp.alignment = TextAlignmentOptions.TopLeft;
                tmp.text = Hint;
                return;
            }

            var legacy = AddLegacyText(rect, 20, color);
            if (legacy != null) legacy.text = Hint;
        }

        static RectTransform CreateRect(RectTransform parent, string name, Vector2 offset, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        static Text AddLegacyText(RectTransform rect, int fontSize, Color color)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = LoadBuiltinFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            return text;
        }

        static bool TmpFontAvailable() => TMP_Settings.defaultFontAsset != null;

        static Font LoadBuiltinFont()
        {
            // Renamed in Unity 2022; try the current name first, then the legacy one.
            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try
                {
                    var font = Resources.GetBuiltinResource<Font>(name);
                    if (font != null) return font;
                }
                catch (System.Exception)
                {
                    // Name not present in this Unity version — fall through to the next one.
                }
            }

            return null;
        }
    }
}
