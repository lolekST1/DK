using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        readonly Line _status = new Line();
        readonly List<ToolButton> _toolButtons = new List<ToolButton>();
        CreatureManager _creatures;

        float _refreshTimer;

        const string Hint = "Click a tool or press 1-4   LMB paint   RMB undo   WASD / screen edge pan   Q/E rotate   Wheel zoom";

        static readonly StringBuilder Builder = new StringBuilder(160);

        static readonly Color ButtonIdle = new Color(0.13f, 0.13f, 0.17f, 0.92f);
        static readonly Color ButtonActive = new Color(0.86f, 0.62f, 0.16f, 0.95f);
        static readonly Color ButtonUnaffordable = new Color(0.13f, 0.13f, 0.17f, 0.55f);
        static readonly Color ButtonLabel = new Color(0.94f, 0.94f, 0.96f);
        static readonly Color ButtonLabelActive = new Color(0.10f, 0.08f, 0.04f);
        static readonly Color ButtonLabelDim = new Color(0.62f, 0.60f, 0.58f);

        const float ButtonWidth = 250f;
        const float ButtonHeight = 52f;
        const float ButtonGap = 10f;
        const float ButtonTop = 92f;

        /// <summary>The tool bar's contents. Costs are read from the catalog, never repeated here.</summary>
        struct ToolSpec
        {
            public PlayerTool Tool;
            public string Label;
            public int Hotkey;
            public RoomType Room;
        }

        static readonly ToolSpec[] Toolbar =
        {
            new ToolSpec { Tool = PlayerTool.Dig, Label = "Dig", Hotkey = 1, Room = RoomType.None },
            new ToolSpec { Tool = PlayerTool.BuildTreasury, Label = "Treasury", Hotkey = 2, Room = RoomType.Treasury },
            new ToolSpec { Tool = PlayerTool.BuildLair, Label = "Lair", Hotkey = 3, Room = RoomType.Lair },
            new ToolSpec { Tool = PlayerTool.Sell, Label = "Sell", Hotkey = 4, Room = RoomType.None },
        };

        /// <summary>One clickable tool bar entry, plus the screen rect the IMGUI fallback draws.</summary>
        class ToolButton
        {
            public ToolSpec Spec;
            public Image Background;
            public readonly Line Label = new Line();
            public Rect ScreenRect;
        }

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
                BuildToolbar(canvas);
            }
            else
            {
                LayOutImGuiToolbar();
            }

            // The tools ask the HUD whether the pointer is busy, so a click on a button does
            // not also paint the tile underneath it.
            if (_tools != null) _tools.PointerOverUi = PointerOverUi;

            if (_resources != null) _resources.GoldChanged += OnGoldChanged;

            RefreshGold();
            RefreshToolbar();
            RefreshStatus();
        }

        void OnDestroy()
        {
            if (_resources != null) _resources.GoldChanged -= OnGoldChanged;
            if (_tools != null) _tools.PointerOverUi = null;
        }

        /// <summary>True while the mouse sits over a tool button, in whichever backend is live.</summary>
        public bool PointerOverUi()
        {
            if (_mode == HudMode.ImGui)
            {
                // IMGUI measures from the top of the screen, Input from the bottom.
                var point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

                for (int i = 0; i < _toolButtons.Count; i++)
                    if (_toolButtons[i].ScreenRect.Contains(point)) return true;

                return false;
            }

            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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

            for (int i = 0; i < _toolButtons.Count; i++)
            {
                var button = _toolButtons[i];
                bool active = _tools.CurrentTool == button.Spec.Tool;
                bool affordable = Affordable(button.Spec);

                button.Label.Set(LabelFor(button.Spec));

                if (button.Background == null) continue;

                button.Background.color = active ? ButtonActive
                                        : affordable ? ButtonIdle
                                        : ButtonUnaffordable;

                SetLabelColor(button.Label, active ? ButtonLabelActive
                                          : affordable ? ButtonLabel
                                          : ButtonLabelDim);
            }
        }

        /// <summary>A build tool you cannot pay for is dimmed rather than hidden.</summary>
        bool Affordable(ToolSpec spec)
        {
            int cost = RoomCatalog.CostOf(spec.Room);
            return cost <= 0 || (_resources != null && _resources.Gold >= cost);
        }

        static string LabelFor(ToolSpec spec)
        {
            int cost = RoomCatalog.CostOf(spec.Room);

            Builder.Length = 0;
            Builder.Append(spec.Label).Append("  [").Append(spec.Hotkey).Append(']');
            if (cost > 0) Builder.Append("  ").Append(cost).Append('g');

            return Builder.ToString();
        }

        static void SetLabelColor(Line line, Color color)
        {
            if (line.Tmp != null) line.Tmp.color = color;
            if (line.Legacy != null) line.Legacy.color = color;
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

            GUI.color = Color.white;
            for (int i = 0; i < _toolButtons.Count; i++)
            {
                var button = _toolButtons[i];
                bool active = _tools != null && _tools.CurrentTool == button.Spec.Tool;
                string label = (active ? "> " : "") + LabelFor(button.Spec);

                if (GUI.Button(button.ScreenRect, label) && _tools != null)
                    _tools.SelectTool(button.Spec.Tool);
            }

            GUI.color = new Color(1f, 0.62f, 0.25f);
            GUI.Label(new Rect(18f, 74f, 1200f, 30f), _status.Value);
            GUI.color = new Color(0.85f, 0.85f, 0.85f, 0.8f);
            GUI.Label(new Rect(18f, 98f, 1200f, 30f), Hint);
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
            var canvasObject = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler),
                                              typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            return (RectTransform)canvasObject.transform;
        }

        /// <summary>
        /// uGUI needs an EventSystem to route clicks, and a procedurally built scene has none.
        /// Legacy input module to match the rest of the game, which reads the old Input class.
        /// </summary>
        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        void BuildLabels(RectTransform canvas)
        {
            BuildLine(canvas, _gold, "Gold Label", -24f, 64f, 46f, 40, new Color(1f, 0.85f, 0.35f));
            BuildLine(canvas, _status, "Status Label", -152f, 44f, 26f, 22, new Color(1f, 0.62f, 0.25f));

            var hint = new Line();
            BuildLine(canvas, hint, "Hint Label", -196f, 44f, 24f, 20, new Color(0.85f, 0.85f, 0.85f, 0.8f));
            hint.Set(Hint);
        }

        /// <summary>
        /// The tool bar proper: one clickable button per tool. It used to be a line of text
        /// that only told you which keys to press, which meant building was invisible to
        /// anyone who had not read the README.
        /// </summary>
        void BuildToolbar(RectTransform canvas)
        {
            for (int i = 0; i < Toolbar.Length; i++)
            {
                var spec = Toolbar[i];
                float x = 24f + i * (ButtonWidth + ButtonGap);

                var rect = CreateRect(canvas, "Tool_" + spec.Label,
                                      new Vector2(x, -ButtonTop), new Vector2(ButtonWidth, ButtonHeight));

                var background = rect.gameObject.AddComponent<Image>();
                background.color = ButtonIdle;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                // We colour the button ourselves from tool state and affordability, so Unity's
                // own tinting would only fight us for it.
                button.transition = Selectable.Transition.None;

                var tool = spec.Tool;
                button.onClick.AddListener(() => { if (_tools != null) _tools.SelectTool(tool); });

                var labelRect = CreateRect(rect, "Label", new Vector2(16f, -12f),
                                           new Vector2(ButtonWidth - 32f, ButtonHeight - 16f));

                var entry = new ToolButton { Spec = spec, Background = background };
                if (_mode == HudMode.TextMeshPro) entry.Label.Tmp = AddTmpText(labelRect, 26f, ButtonLabel);
                else entry.Label.Legacy = AddLegacyText(labelRect, 22, ButtonLabel);

                _toolButtons.Add(entry);
            }
        }

        /// <summary>Screen rects for the IMGUI fallback, which draws its own buttons.</summary>
        void LayOutImGuiToolbar()
        {
            for (int i = 0; i < Toolbar.Length; i++)
                _toolButtons.Add(new ToolButton
                {
                    Spec = Toolbar[i],
                    ScreenRect = new Rect(18f + i * 178f, 40f, 170f, 28f),
                });
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
            // Otherwise the full-width HUD lines swallow every click aimed at the map.
            text.raycastTarget = false;
            return text;
        }

        static Text AddLegacyText(RectTransform rect, int fontSize, Color color)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = LoadBuiltinFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false;
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
