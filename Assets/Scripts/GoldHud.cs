using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DK
{
    /// <summary>
    /// Builds the on-screen gold counter at runtime. Prefers TextMeshPro, drops to legacy
    /// uGUI text when the project has no TMP font asset, and finally to IMGUI when even the
    /// built-in font is unavailable — the counter is an acceptance criterion, so it must
    /// survive a project that has imported nothing.
    /// </summary>
    public class GoldHud : MonoBehaviour
    {
        enum HudMode { TextMeshPro, LegacyText, ImGui }

        ResourceManager _resources;
        HudMode _mode;
        TextMeshProUGUI _tmpLabel;
        Text _legacyLabel;
        string _goldText = "Gold: 0";

        const string Hint = "LMB drag: mark for digging   RMB: cancel   WASD / screen edge: pan   Q/E: rotate   Wheel: zoom";

        public void Configure(ResourceManager resources)
        {
            _resources = resources;
            _mode = ResolveMode();

            if (_mode != HudMode.ImGui)
            {
                var canvas = BuildCanvas();
                BuildLabels(canvas);
            }

            _resources.GoldChanged += OnGoldChanged;
            OnGoldChanged(_resources.Gold);
        }

        void OnDestroy()
        {
            if (_resources != null) _resources.GoldChanged -= OnGoldChanged;
        }

        void OnGoldChanged(int gold)
        {
            _goldText = $"Gold: {gold}";
            if (_tmpLabel != null) _tmpLabel.text = _goldText;
            if (_legacyLabel != null) _legacyLabel.text = _goldText;
        }

        void OnGUI()
        {
            if (_mode != HudMode.ImGui) return;

            GUI.color = new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(18f, 14f, 400f, 30f), _goldText);
            GUI.color = new Color(0.85f, 0.85f, 0.85f, 0.8f);
            GUI.Label(new Rect(18f, 38f, 1200f, 30f), Hint);
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- construction

        static HudMode ResolveMode()
        {
            if (TmpFontAvailable()) return HudMode.TextMeshPro;
            if (LoadBuiltinFont() != null) return HudMode.LegacyText;

            Debug.LogWarning("[DK] No TextMeshPro font asset and no built-in font — falling back to " +
                             "IMGUI for the gold counter. Import TMP Essential Resources via " +
                             "Window > TextMeshPro for nicer text.");
            return HudMode.ImGui;
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

        void BuildLabels(RectTransform canvas)
        {
            var goldColor = new Color(1f, 0.85f, 0.35f);
            var hintColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);

            var goldRect = CreateRect(canvas, "Gold Label", new Vector2(24f, -24f), new Vector2(520f, 72f));
            var hintRect = CreateRect(canvas, "Hint Label", new Vector2(24f, -96f), new Vector2(1400f, 48f));

            if (_mode == HudMode.TextMeshPro)
            {
                _tmpLabel = AddTmpText(goldRect, 46f, goldColor);
                AddTmpText(hintRect, 24f, hintColor).text = Hint;
                return;
            }

            _legacyLabel = AddLegacyText(goldRect, 40, goldColor);
            AddLegacyText(hintRect, 20, hintColor).text = Hint;
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

        static TextMeshProUGUI AddTmpText(RectTransform rect, float fontSize, Color color)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            return text;
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

        /// <summary>
        /// True only when TMP can actually render. Reading TMP_Settings throws outright when
        /// TMP Essential Resources have never been imported, so this has to be defensive
        /// rather than a plain null check.
        /// </summary>
        static bool TmpFontAvailable()
        {
            try
            {
                return TMP_Settings.defaultFontAsset != null;
            }
            catch (System.Exception)
            {
                // TMP_Settings.instance is null until TMP Essential Resources are imported,
                // and the property dereferences it without checking.
                return false;
            }
        }

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
