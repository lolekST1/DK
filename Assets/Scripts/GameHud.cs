using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DK
{
    /// <summary>
    /// Builds the on-screen readout at runtime: gold against vault capacity, the tool bar, and
    /// a status line that explains why the tile under the cursor just refused you.
    ///
    /// Prefers TextMeshPro, drops to legacy uGUI text when the project has no TMP font asset,
    /// and finally to IMGUI when even the built-in font is unavailable — the counter is an
    /// acceptance criterion, so it must survive a project that has imported nothing. Every
    /// line is plain text for that reason: rich-text markup would leak as literal tags in the
    /// IMGUI fallback.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        enum HudMode { TextMeshPro, LegacyText, ImGui }

        ResourceManager _resources;
        RoomManager _rooms;
        PlayerTools _tools;
        IReadOnlyList<ImpAI> _imps;

        HudMode _mode;
        readonly Line _gold = new Line();
        readonly Line _toolbar = new Line();
        readonly Line _status = new Line();
        CreatureManager _creatures;

        float _refreshTimer;

        const string Hint = "LMB paint   RMB undo   1-4 pick tool   WASD / screen edge pan   Q/E rotate   Wheel zoom";

        static readonly StringBuilder Builder = new StringBuilder(160);

        /// <summary>One HUD row, in whichever of the three text backends is available.</summary>
        class Line
        {
            public TextMeshProUGUI Tmp;
            public Text Legacy;
            public string Value = string.Empty;

            public void Set(string value)
            {
                if (Value == value) return;

                Value = value;
                if (Tmp != null) Tmp.text = value;
                if (Legacy != null) Legacy.text = value;
            }
        }

        public void Configure(ResourceManager resources, RoomManager rooms, PlayerTools tools,
                              IReadOnlyList<ImpAI> imps, CreatureManager creatures)
        {
            _resources = resources;
            _rooms = rooms;
            _tools = tools;
            _imps = imps;
            _creatures = creatures;
            _mode = ResolveMode();

            if (_mode != HudMode.ImGui)
            {
                var canvas = BuildCanvas();
                BuildLabels(canvas);
            }

            if (_resources != null) _resources.GoldChanged += OnGoldChanged;

            RefreshGold();
            RefreshToolbar();
            RefreshStatus();
        }

        void OnDestroy()
        {
            if (_resources != null) _resources.GoldChanged -= OnGoldChanged;
        }

        void OnGoldChanged(int gold, int capacity) => RefreshGold();

        void Update()
        {
            // The toolbar and status line depend on mouse hover and on how much gold the imps
            // are holding, neither of which raises an event. A few refreshes a second is
            // plenty and keeps string building off the per-frame path.
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.1f;

            RefreshGold();
            RefreshToolbar();
            RefreshStatus();
        }

        // ---------------------------------------------------------------- content

        void RefreshGold()
        {
            if (_resources == null) return;

            Builder.Length = 0;
            Builder.Append("Gold ").Append(_resources.Gold).Append(" / ").Append(_resources.Capacity);

            int carried = CarriedGold();
            if (carried > 0) Builder.Append("   (+").Append(carried).Append(" in hand)");

            if (_creatures != null && _creatures.CreatureCount > 0)
            {
                Builder.Append("   Creatures ").Append(_creatures.CreatureCount);
                Builder.Append("   Payday ").Append(Mathf.CeilToInt(_creatures.SecondsToPayday)).Append('s');
            }

            _gold.Set(Builder.ToString());
        }

        void RefreshToolbar()
        {
            if (_tools == null) return;

            Builder.Length = 0;
            AppendTool(PlayerTool.Dig, "Dig", 1, 0);
            AppendTool(PlayerTool.BuildTreasury, "Treasury", 2, RoomCatalog.CostOf(RoomType.Treasury));
            AppendTool(PlayerTool.BuildLair, "Lair", 3, RoomCatalog.CostOf(RoomType.Lair));
            AppendTool(PlayerTool.Sell, "Sell", 4, 0);

            _toolbar.Set(Builder.ToString());
        }

        void AppendTool(PlayerTool tool, string label, int hotkey, int cost)
        {
            bool active = _tools.CurrentTool == tool;

            Builder.Append(active ? "  > " : "   ");
            Builder.Append(label).Append(' ').Append('[').Append(hotkey).Append(']');
            if (cost > 0) Builder.Append(' ').Append(cost).Append('g');
            if (active) Builder.Append(" <");
        }

        void RefreshStatus()
        {
            _status.Set(BuildStatus());
        }

        string BuildStatus()
        {
            // Whatever the cursor is refusing right now is the most useful thing to say.
            if (_tools != null && _tools.CurrentTool != PlayerTool.Dig && _tools.HoverRefusal != null)
                return "Cannot place here: " + _tools.HoverRefusal;

            int carried = CarriedGold();
            if (carried > 0 && _rooms != null && _rooms.FreeCapacity == 0)
                return "Vaults are full — imps are standing around holding " + carried +
                       " gold. Build a treasury [2].";

            // Creatures walking out is the most expensive thing that can be going wrong.
            if (_creatures != null && _creatures.CreatureCount > 0 &&
                _resources != null && _resources.Gold < WageBill())
                return "Cannot make payroll — " + WageBill() + " gold due, " + _resources.Gold +
                       " in the vault. Creatures leave after three missed paydays.";

            if (_creatures != null && _creatures.ArrivalBlocker != null &&
                _rooms != null && _rooms.HasPortal)
                return "Portal: " + _creatures.ArrivalBlocker;

            if (_rooms != null && _rooms.LairCount == 0)
                return "No lair yet. Imps with a lair dig faster [3].";

            if (_resources != null && _resources.TotalSpilled > 0)
                return "Spilled " + _resources.TotalSpilled + " gold with nowhere to store it.";

            return string.Empty;
        }

        /// <summary>What the next payday will cost, so the warning can name a number.</summary>
        int WageBill()
        {
            if (_creatures == null) return 0;

            int total = 0;
            for (int i = 0; i < _creatures.Creatures.Count; i++)
            {
                var creature = _creatures.Creatures[i];
                if (creature != null) total += creature.Wage;
            }

            return total;
        }

        int CarriedGold()
        {
            if (_imps == null) return 0;

            int total = 0;
            for (int i = 0; i < _imps.Count; i++)
                if (_imps[i] != null) total += _imps[i].CarriedGold;

            return total;
        }

        void OnGUI()
        {
            if (_mode != HudMode.ImGui) return;

            GUI.color = new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(18f, 14f, 600f, 30f), _gold.Value);
            GUI.color = new Color(0.92f, 0.92f, 0.95f);
            GUI.Label(new Rect(18f, 38f, 1200f, 30f), _toolbar.Value);
            GUI.color = new Color(1f, 0.62f, 0.25f);
            GUI.Label(new Rect(18f, 62f, 1200f, 30f), _status.Value);
            GUI.color = new Color(0.85f, 0.85f, 0.85f, 0.8f);
            GUI.Label(new Rect(18f, 86f, 1200f, 30f), Hint);
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- construction

        static HudMode ResolveMode()
        {
            if (TmpFontAvailable()) return HudMode.TextMeshPro;
            if (LoadBuiltinFont() != null) return HudMode.LegacyText;

            Debug.LogWarning("[DK] No TextMeshPro font asset and no built-in font — falling back to " +
                             "IMGUI for the HUD. Import TMP Essential Resources via " +
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
            BuildLine(canvas, _gold, "Gold Label", -24f, 64f, 46f, 40, new Color(1f, 0.85f, 0.35f));
            BuildLine(canvas, _toolbar, "Toolbar Label", -88f, 44f, 28f, 24, new Color(0.92f, 0.92f, 0.95f));
            BuildLine(canvas, _status, "Status Label", -132f, 44f, 26f, 22, new Color(1f, 0.62f, 0.25f));

            var hint = new Line();
            BuildLine(canvas, hint, "Hint Label", -176f, 44f, 24f, 20, new Color(0.85f, 0.85f, 0.85f, 0.8f));
            hint.Set(Hint);
        }

        void BuildLine(RectTransform canvas, Line line, string name, float y, float height,
                       float tmpSize, int legacySize, Color color)
        {
            var rect = CreateRect(canvas, name, new Vector2(24f, y), new Vector2(1600f, height));

            if (_mode == HudMode.TextMeshPro) line.Tmp = AddTmpText(rect, tmpSize, color);
            else line.Legacy = AddLegacyText(rect, legacySize, color);

            line.Value = null;
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
