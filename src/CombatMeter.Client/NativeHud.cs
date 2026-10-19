using CombatMeter.Core;
using CombatMeter.Shared;
using ProjectM.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CombatMeter.Client;

internal sealed class NativeHud : IDisposable
{
    private const float HudWidth = 320, HudHeight = 136, HudListWidth = 308, HudRowPitch = 24;
    private const float ReviewWidth = 830, ReviewHeight = 760, HistoryWidth = 204, ReviewListWidth = 474;
    private const float DetailWidth = 650, DetailHeight = 560, SourceListWidth = 538;
    private const float WindowInset = 48, ScrollbarGutter = 16;
    private const float TextScale = 1.2f, RowPitch = 30;
    private static readonly Color ButtonRed = new(0.36f, 0.09f, 0.12f, 1);
    private static readonly Color SelectedButtonRed = new(0.55f, 0.13f, 0.17f, 1);
    private sealed record ScrollView(RectTransform Viewport, RectTransform Content, ScrollRect Scroll, RectTransform? Track, RectTransform? Thumb);
    private sealed record Row(GameObject Root, Image Bar, TextMeshProUGUI Name, TextMeshProUGUI Value)
    {
        public DetailSelection? Selection;
        public string ActorName = "";
        public string EncounterLabel = "";
        public Image? Icon;
        public Image? SourceMarker;
    }
    private readonly ClientRuntime _runtime;
    private readonly GameObject _meter;
    private readonly GameObject _review;
    private readonly GameObject _details;
    private readonly RectTransform _detailRect;
    private readonly RectTransform _detailHeader;
    private readonly RectTransform _detailContent;
    private readonly TextMeshProUGUI _detailTitle;
    private readonly TextMeshProUGUI _detailSubtitle;
    private readonly TextMeshProUGUI _detailMessage;
    private readonly (Button Button, TextMeshProUGUI Text) _detailFilter;
    private readonly List<Row> _sourceRows = new();
    private readonly SourceVisuals _sourceVisuals = new();
    private readonly HudIcons _icons;
    private DetailSelection? _pinned;
    private (DetailSelection Selection, string Name, string Label)? _pressedActor;
    private bool _detailDragging;
    private bool _dismissPointerClick;
    private int _dismissReleasedFrame = -1;
    private Vector2 _detailDragStart;
    private Vector2 _detailInitial;
    private string? _hoverKey;
    private double _hoverStart;
    private string? _tooltipText;
    private Vector3 _tooltipPosition;
    private readonly RectTransform _meterRect;
    private readonly RectTransform _header;
    private readonly RectTransform _meterContent;
    private readonly RectTransform _reviewContent;
    private readonly RectTransform _historyContent;
    private readonly Image _meterBackground;
    private readonly TMP_FontAsset _font;
    private readonly Material _fontMaterial;
    private readonly Dictionary<int, Material> _outlinedFonts = new();
    private readonly Image? _windowTemplate;
    private readonly Image? _windowHeaderTemplate;
    private readonly Sprite? _trimmedWindowSprite;
    private readonly Sprite? _barSprite;
    private readonly List<Row> _meterRows = new();
    private readonly List<Row> _reviewRows = new();
    private readonly List<(Button Button, TextMeshProUGUI Text)> _historyButtons = new();
    private readonly List<(Button Button, TextMeshProUGUI Text, EncounterKind Kind)> _modeButtons = new();
    private readonly TextMeshProUGUI _meterDetail;
    private readonly TextMeshProUGUI _reviewTitle;
    private readonly TextMeshProUGUI _reviewDetail;
    private readonly TextMeshProUGUI _enemyLabel;
    private readonly TextMeshProUGUI _filterLabel;
    private readonly TextMeshProUGUI _lockLabel;
    private readonly TextMeshProUGUI _visibleLabel;
    private readonly RectTransform _openButton;
    private readonly RectTransform _lockButton;
    private readonly Image _lockIcon;
    private readonly Image _cogIcon;
    private readonly List<(Button Button, Action Action)> _buttons = new();
    private readonly List<ScrollView> _scrolls = new();
    private ScrollView? _draggedScroll;
    private float _scrollGrab;
    private Button? _pressedButton;
    private int _keyboardButton = -1;
    private readonly FakeTooltip? _tooltip;
    private readonly List<(RectTransform Target, string Title, string Description)> _tooltips = new();
    private RectTransform? _hovered;
    private string? _selectedEncounter;
    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _initialPosition;
    public bool ReviewOpen { get; private set; }
    public bool DetailsOpen => _pinned is not null;
    public bool IsAlive => _meter != null && _review != null && _details != null;
    public bool CapturesWheel => _draggedScroll != null || _scrolls.Any(x => x.Viewport.gameObject.activeInHierarchy
        && InActiveLayer(x.Viewport) && (PointerVisible(x.Viewport) || x.Track != null && PointerVisible(x.Track)));
    public bool WantsInputFocus => ReviewOpen || DetailsOpen || _dismissPointerClick || _dragging || _pressedButton != null || _draggedScroll != null
        || (Input.mouseScrollDelta.y != 0 && _scrolls.Any(x => x.Viewport.gameObject.activeInHierarchy && ContainsPointer(x.Viewport)))
        || (Input.GetMouseButton(0) && _meter.activeInHierarchy
        && (ContainsPointer(_openButton) || ContainsPointer(_lockButton)
            || (!_runtime.Locked.Value && ContainsPointer(_header))
            || _meterRows.Any(x => x.Selection is not null && PointerVisible(x.Root.GetComponent<RectTransform>()))));

    private static bool ContainsPointer(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
    }
    private static bool PointerVisible(RectTransform rect)
    {
        if (!rect.gameObject.activeInHierarchy || !ContainsPointer(rect)) return false;
        for (var parent = rect.parent; parent != null; parent = parent.parent)
            if (parent.GetComponent<RectMask2D>() != null && !ContainsPointer(parent.Cast<RectTransform>())) return false;
        return true;
    }

    public NativeHud(ClientRuntime runtime, UICanvasBase canvas)
    {
        _runtime = runtime;
        var labels = canvas.HUDParent.GetComponentsInChildren<TextMeshProUGUI>(true);
        var template = labels.FirstOrDefault(x => x.font != null) ?? throw new InvalidOperationException("Native HUD font is not available yet");
        _font = template.font; _fontMaterial = OutlinedFont(template.fontSharedMaterial);
        runtime.LogStyle($"Text outline: {_fontMaterial.shader.name}; supported={_fontMaterial.HasProperty("_OutlineWidth")}");
        var nativeTooltip = canvas.TooltipsParent.GetComponentInChildren<FakeTooltip>(true);
        if (nativeTooltip != null)
        {
            _tooltip = Object.Instantiate(nativeTooltip, canvas.TooltipsParent, false);
            _tooltip.name = "CombatMeter Tooltip";
            _tooltip.UseAdditionalTooltip = false;
            _tooltip.ClampWithinRect = canvas.transform.Cast<RectTransform>();
            foreach (var graphic in _tooltip.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var label in _tooltip.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                label.richText = true;
                if (label.font != null && label.fontSharedMaterial != null)
                {
                    label.fontSharedMaterial = OutlinedFont(label.fontSharedMaterial);
                    label.UpdateMeshPadding();
                }
            }
            _tooltip.DisableTooltip();
        }
        var visuals = canvas.ArenaSummaryParentPrefab != null
            ? canvas.ArenaSummaryParentPrefab.GetComponentsInChildren<Image>(true)
            : canvas.HUDParent.GetComponentsInChildren<Image>(true);
        _barSprite = visuals.FirstOrDefault(x => x.sprite != null && x.name.Contains("Fill", StringComparison.OrdinalIgnoreCase))?.sprite;
        var menuImages = new List<Image>();
        foreach (var mapping in canvas.HUDMenus)
            if (mapping.Prefab != null) menuImages.AddRange(mapping.Prefab.GetComponentsInChildren<Image>(true));
        _windowTemplate = menuImages.FirstOrDefault(x => x.sprite != null && x.sprite.name == "Window_Background");
        _windowHeaderTemplate = menuImages.FirstOrDefault(x => x.sprite != null && x.sprite.name == "Window_Background_Header");
        runtime.LogStyle($"Native window: {_windowTemplate?.sprite?.name ?? "solid fallback"}; header: {_windowHeaderTemplate?.sprite?.name ?? "none"}");
        if (_windowTemplate != null)
        {
            // This installed native sprite includes 52 pixels of black below
            // its actual border. Trim our own sprite, never the shared asset.
            var original = _windowTemplate.sprite;
            var region = original.textureRect;
            var border = original.border;
            var padding = UnityEngine.Sprites.DataUtility.GetPadding(original);
            const float bottomTrim = 52;
            if (Mathf.Approximately(region.height, 241) && Mathf.Approximately(border.y, 100))
            {
                region.y += bottomTrim; region.height -= bottomTrim;
                border = new Vector4(Math.Max(0, border.x - padding.x), border.y - bottomTrim,
                    Math.Max(0, border.z - padding.z), Math.Max(0, border.w - padding.w));
                _trimmedWindowSprite = Sprite.Create(original.texture, region, new Vector2(0.5f, 0.5f),
                    original.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                _trimmedWindowSprite.name = "CombatMeter Native Window (trimmed)";
            }
        }
        _icons = new HudIcons();
        _meter = Panel("CombatMeter HUD", canvas.HUDParent, HudWidth, HudHeight, new Color(0.055f, 0.045f, 0.055f, 0), false);
        _meterRect = _meter.GetComponent<RectTransform>();
        _meterRect.anchorMin = _meterRect.anchorMax = _meterRect.pivot = new Vector2(1, 1);
        _meterBackground = _meter.GetComponent<Image>();
        _header = Box("Drag strip", _meter.transform, 32, 2, HudWidth - 64, 26);
        var headerHitArea = _header.gameObject.AddComponent<Image>();
        headerHitArea.color = Color.clear;
        headerHitArea.raycastTarget = false;
        _meterDetail = Text(_header, "Waiting for a fight", 2, 0, HudWidth - 68, 26, 12);
        _meterContent = Scroll(_meter.transform, 6, 32, HudListWidth, 96, false);
        var settings = Button(_meter.transform, "", 6, 5, 20, 20, ToggleReview, false);
        _openButton = settings.Button.GetComponent<RectTransform>();
        _cogIcon = AddIcon(_openButton, _icons.Cog);
        var lockingToggle = Button(_meter.transform, "", HudWidth - 26, 5, 20, 20, ToggleLock, false);
        _lockButton = lockingToggle.Button.GetComponent<RectTransform>();
        _lockIcon = AddIcon(_lockButton, _icons.Unlocked);
        _openButton.gameObject.SetActive(false); _lockButton.gameObject.SetActive(false);
        _tooltips.Add((_openButton, "CombatMeter", "Review this fight, recent encounters, and display settings. The shortcut is configurable in the client config."));
        _tooltips.Add((_lockButton, "Lock meter position", "Click to lock or unlock dragging. A closed padlock means the meter position is locked."));
        _tooltips.Add((_header, "Move CombatMeter", "Drag this header to move the meter. Position is saved when you release the mouse. Lock the position in Review / settings."));

        // HUDMenuParent is hidden by the native menu controller during combat.
        // Keep this panel on the HUD so its button and F8 can always open it.
        _review = Panel("CombatMeter Review", canvas.HUDParent, ReviewWidth, ReviewHeight, new Color(0.05f, 0.04f, 0.055f, 0.98f));
        var reviewRect = _review.GetComponent<RectTransform>();
        reviewRect.anchorMin = reviewRect.anchorMax = reviewRect.pivot = new Vector2(0.5f, 0.5f);
        reviewRect.anchoredPosition = Vector2.zero;
        _reviewTitle = Text(_review.transform, "CombatMeter", WindowInset, 64, 632, 34, 24);
        Button(_review.transform, "Close", 698, 64, 84, 28, CloseReview);
        _reviewDetail = Text(_review.transform, "", WindowInset, 110, ReviewWidth - WindowInset * 2, 24, 14);
        var enemies = Button(_review.transform, "", WindowInset, 148, 170, 30, () => { _runtime.ShowEnemies.Value = !_runtime.ShowEnemies.Value; Refresh(); }); _enemyLabel = enemies.Text;
        var filter = Button(_review.transform, "", 228, 148, 210, 30, CycleFilter); _filterLabel = filter.Text;
        _tooltips.Add((enemies.Button.GetComponent<RectTransform>(), "Enemy damage", "Show damage dealt by monsters, bosses, and boss summons to players. Enemy totals are separate from player damage and shares."));
        _tooltips.Add((filter.Button.GetComponent<RectTransform>(), "Boss targets", "Switch between damage to the boss alone and damage to the boss plus its owned summons. Unrelated nearby enemies are excluded."));
        Text(_review.transform, "ENCOUNTERS", WindowInset, 194, HistoryWidth, 22, 14);
        Text(_review.transform, "DAMAGE · DPS", 292, 194, ReviewListWidth, 22, 14);
        var historyContent = Scroll(_review.transform, WindowInset, 220, HistoryWidth, 252);
        _historyContent = historyContent;
        for (var i = 0; i < Protocol.HistoryLimit + 1; i++)
        {
            var index = i;
            _historyButtons.Add(Button(historyContent, "", 0, i * 36, HistoryWidth, 30, () => SelectHistory(index)));
        }
        historyContent.sizeDelta = new Vector2(HistoryWidth, (Protocol.HistoryLimit + 1) * 36);
        _reviewContent = Scroll(_review.transform, 292, 220, ReviewListWidth, 252);
        Text(_review.transform, "DISPLAY SETTINGS", WindowInset, 500, ReviewWidth - WindowInset * 2, 24, 14);
        var visibility = Button(_review.transform, "", WindowInset, 536, 122, 30, () => { _runtime.Visible.Value = !_runtime.Visible.Value; Refresh(); }); _visibleLabel = visibility.Text;
        var locking = Button(_review.transform, "", 178, 536, 124, 30, ToggleLock); _lockLabel = locking.Text;
        Button(_review.transform, "Scale −", 310, 536, 96, 30, () => { _runtime.Scale.Value = Math.Max(0.6f, _runtime.Scale.Value - 0.1f); Refresh(); });
        Button(_review.transform, "Scale +", 414, 536, 96, 30, () => { _runtime.Scale.Value = Math.Min(2f, _runtime.Scale.Value + 0.1f); Refresh(); });
        var opacityDown = Button(_review.transform, "BG opacity −", 518, 536, 128, 30, () => { _runtime.BackgroundOpacity.Value = Math.Max(0f, _runtime.BackgroundOpacity.Value - 0.1f); Refresh(); });
        var opacityUp = Button(_review.transform, "BG opacity +", 654, 536, 128, 30, () => { _runtime.BackgroundOpacity.Value = Math.Min(1f, _runtime.BackgroundOpacity.Value + 0.1f); Refresh(); });
        _tooltips.Add((opacityDown.Button.GetComponent<RectTransform>(), "DPS list background", "Reduce background opacity. At zero the background is fully transparent; text, bars and icons remain visible."));
        _tooltips.Add((opacityUp.Button.GetComponent<RectTransform>(), "DPS list background", "Increase background opacity. This only affects the DPS list background."));
        var modes = Enum.GetValues<EncounterKind>();
        for (var i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            var button = Button(_review.transform, "", WindowInset + i * 148, 582, 140, 32, () => { _runtime.Modes[mode].Value = !_runtime.Modes[mode].Value; Refresh(); });
            _modeButtons.Add((button.Button, button.Text, mode));
            _tooltips.Add((button.Button.GetComponent<RectTransform>(), $"{mode} visibility",
                "Controls display only. If the label says 'server off', tracking is disabled in the server's CombatMeter config. Enable that mode under [Tracking] and restart the server. 'Waiting' means server policy has not arrived yet."));
        }
        Text(_review.transform, "Session history · Drag the unlocked HUD to move it.", WindowInset, 654, ReviewWidth - WindowInset * 2, 24, 13);
        _review.SetActive(false);
        _details = Panel("CombatMeter Damage Sources", canvas.HUDParent, DetailWidth, DetailHeight, new Color(0.05f, 0.04f, 0.055f, 0.98f));
        _detailRect = _details.GetComponent<RectTransform>();
        _detailRect.anchorMin = _detailRect.anchorMax = _detailRect.pivot = new Vector2(0.5f, 0.5f);
        _detailRect.anchoredPosition = Vector2.zero;
        _detailHeader = Box("Detail drag strip", _details.transform, WindowInset, 60, 460, 46);
        _detailTitle = Text(_detailHeader, "Damage sources", 0, 4, 450, 34, 22);
        Button(_details.transform, "Close", 524, 64, 78, 28, CloseDetails);
        _detailSubtitle = Text(_details.transform, "", WindowInset, 110, DetailWidth - WindowInset * 2, 24, 14);
        _detailFilter = Button(_details.transform, "", WindowInset, 146, 210, 28, CycleFilter);
        Text(_details.transform, "DAMAGE · SHARE", WindowInset + SourceListWidth - 158, 148, 150, 24, 13).alignment = TextAlignmentOptions.MidlineRight;
        _detailContent = Scroll(_details.transform, WindowInset, 192, SourceListWidth, 270);
        _detailMessage = Text(_details.transform, "", WindowInset + 8, 208, SourceListWidth - 8, 54, 16);
        _details.SetActive(false);
        Refresh();
    }

    private RectTransform Box(string name, Transform parent, float x, float y, float width, float height)
    {
        var gameObject = new GameObject(name);
        var rect = gameObject.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private Image AddIcon(RectTransform button, Sprite sprite)
    {
        var icon = Box(sprite.name, button, 2, 2, 16, 16).gameObject.AddComponent<Image>();
        icon.sprite = sprite; icon.preserveAspect = true;
        icon.color = new Color(0.9f, 0.86f, 0.79f); icon.raycastTarget = false;
        return icon;
    }

    private GameObject Panel(string name, Transform parent, float width, float height, Color color, bool decorate = true)
    {
        var rect = Box(name, parent, 0, 0, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        // Native background sprites can fade to zero alpha at their edges.
        // Keep an opaque base beneath the decorative native texture.
        image.color = color;
        image.raycastTarget = false;
        if (decorate && _windowTemplate != null)
        {
            // Copy only native visuals. Shared prefabs and their controllers
            // remain untouched; the native nine-slice keeps its actual border.
            image.color = Color.clear;
            var backing = Box("Window backing", rect, 20, 20, width - 40, height - 40).gameObject.AddComponent<Image>();
            backing.color = new Color(0.025f, 0.028f, 0.032f, 0.98f); backing.raycastTarget = false;
            CopyWindowImage(_windowTemplate, rect, "Native window frame", 8, 8, width - 16, height - 16);
            if (_windowHeaderTemplate != null)
                CopyWindowImage(_windowHeaderTemplate, rect, "Native window header", 20, 12, width - 40, 48);
        }
        return rect.gameObject;
    }

    private void CopyWindowImage(Image template, Transform parent, string name, float x, float y, float width, float height)
    {
        var image = Box(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = template == _windowTemplate && _trimmedWindowSprite != null ? _trimmedWindowSprite : template.sprite;
        image.material = template.material;
        image.type = template.type; image.color = template.color;
        image.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier;
        image.fillCenter = template.fillCenter; image.raycastTarget = false;
    }

    private Material OutlinedFont(Material source)
    {
        var id = source.GetInstanceID();
        if (_outlinedFonts.TryGetValue(id, out var existing)) return existing;
        // Share one private material per native font/atlas. Never change the
        // game's material, and preserve each tooltip font's original texture.
        var material = new Material(source) { name = source.name + " (CombatMeter outline)" };
        _outlinedFonts[id] = material;
        if (material.HasProperty("_OutlineColor") && material.HasProperty("_OutlineWidth"))
        {
            material.SetColor("_OutlineColor", new Color(0.008f, 0.006f, 0.009f, 1));
            // A thick, hard SDF outline exaggerates atlas steps at small HUD sizes.
            // Keep the dark contour, with a softer edge and neutral SDF sharpness.
            material.SetFloat("_OutlineWidth", 0.16f);
            if (material.HasProperty("_OutlineSoftness")) material.SetFloat("_OutlineSoftness", 0.06f);
            if (material.HasProperty("_Sharpness")) material.SetFloat("_Sharpness", 0);
            material.EnableKeyword("OUTLINE_ON");
            ShaderUtilities.UpdateShaderRatios(material);
        }
        return material;
    }

    private TextMeshProUGUI Text(Transform parent, string value, float x, float y, float width, float height, float size)
    {
        var rect = Box("Text", parent, x, y, width, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _font; text.fontSharedMaterial = _fontMaterial;
        text.fontSize = size * TextScale; text.color = new Color(0.97f, 0.94f, 0.88f);
        text.richText = false; text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.text = value;
        text.UpdateMeshPadding();
        return text;
    }

    private (UnityEngine.UI.Button Button, TextMeshProUGUI Text) Button(Transform parent, string label, float x, float y, float width, float height, Action action, bool showBackground = true)
    {
        var rect = Box("Button", parent, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        // A fading native panel sprite is unsuitable as a button fill.
        // A plain Image keeps the entire button opaque, including its edges.
        image.color = showBackground ? ButtonRed : Color.clear;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        if (!showBackground) button.transition = Selectable.Transition.None;
        // The game controls native UI event routing. Poll only our own button
        // rectangles so a hidden menu or combat input context cannot drop clicks.
        // Do not also register onClick: that would execute a click twice.
        _buttons.Add((button, action));
        var text = Text(rect, label, 4, 2, width - 8, height - 4, 13);
        text.alignment = TextAlignmentOptions.Center;
        // Keep narrow controls readable without truncating their labels.
        text.enableAutoSizing = true; text.fontSizeMin = 14; text.fontSizeMax = 13 * TextScale;
        return (button, text);
    }

    private RectTransform Scroll(Transform parent, float x, float y, float width, float height, bool showScrollbar = true)
    {
        var rect = Box("Scroll view", parent, x, y, width + (showScrollbar ? ScrollbarGutter : 0), height);
        var viewport = Box("Viewport", rect, 0, 0, width, height);
        var background = viewport.gameObject.AddComponent<Image>();
        // The game's DefaultUI shader does not clip these Images like TMP
        // text under RectMask2D. Use a stencil mask for the complete row.
        background.color = Color.white; background.raycastTarget = false;
        var stencil = viewport.gameObject.AddComponent<Mask>();
        stencil.showMaskGraphic = false;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Box("Content", viewport, 0, 0, width, height);
        var scroll = rect.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 22;
        scroll.enabled = false; // Our scoped input routing scrolls these contents once.
        RectTransform? track = null, thumb = null;
        if (showScrollbar)
        {
            track = Box("Scrollbar track", rect, width + 4, 0, 12, height);
            var rail = track.gameObject.AddComponent<Image>();
            rail.color = new Color(0.15f, 0.14f, 0.15f, 1); rail.raycastTarget = false;
            thumb = Box("Scrollbar thumb", track, 2, 0, 8, height);
            var handle = thumb.gameObject.AddComponent<Image>();
            handle.color = new Color(0.64f, 0.59f, 0.49f, 1); handle.raycastTarget = false;
            track.gameObject.SetActive(false);
        }
        _scrolls.Add(new(viewport, content, scroll, track, thumb));
        return content;
    }

    private static ScrollGeometry Geometry(ScrollView view) =>
        ScrollGeometry.Calculate(view.Content.rect.height, view.Viewport.rect.height, view.Content.anchoredPosition.y);

    private void RefreshScrollbars()
    {
        foreach (var view in _scrolls)
        {
            var geometry = Geometry(view);
            var position = view.Content.anchoredPosition;
            position.y = Mathf.Clamp(position.y, 0, geometry.Range); view.Content.anchoredPosition = position;
            if (view.Track == null || view.Thumb == null) continue;
            view.Track.gameObject.SetActive(geometry.CanScroll);
            view.Thumb.sizeDelta = new Vector2(8, geometry.ThumbHeight);
            view.Thumb.anchoredPosition = new Vector2(2, -geometry.ThumbTop);
            view.Thumb.GetComponent<Image>().color = view == _draggedScroll || PointerVisible(view.Track)
                ? new Color(0.94f, 0.85f, 0.65f, 1) : new Color(0.64f, 0.59f, 0.49f, 1);
        }
    }

    private static float TrackPointer(RectTransform track)
    {
        var canvas = track.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(track, Input.mousePosition, camera, out var local);
        return -local.y;
    }

    private Row CreateRow(Transform parent, float width, bool interactive = true, bool compact = false)
    {
        var rect = Box("Player", parent, 0, 0, width, compact ? 24 : 28);
        var fill = Box("Damage bar", rect, 0, 1, width, compact ? 22 : 26);
        var image = fill.gameObject.AddComponent<Image>();
        image.sprite = _barSprite; image.type = Image.Type.Sliced; image.raycastTarget = false;
        var name = Text(rect, "", 6, compact ? 1 : 2, width * 0.6f, compact ? 22 : 25, compact ? 12 : 13);
        var value = Text(rect, "", width * 0.62f, compact ? 1 : 2, width * 0.36f, compact ? 22 : 25, compact ? 12 : 13);
        value.alignment = TextAlignmentOptions.MidlineRight;
        var row = new Row(rect.gameObject, image, name, value);
        if (interactive)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image; button.transition = Selectable.Transition.None;
            _buttons.Add((button, () => { if (row.Selection is { } selection) OpenDetails(selection, row.ActorName, row.EncounterLabel); }));
        }
        return row;
    }

    private void RenderRows(List<Row> rows, RectTransform content, EncounterSnapshot? fight, float width, bool compact = false)
    {
        var players = fight is null ? Array.Empty<MeterRow>() : MeterPresentation.Players(fight, _runtime.Filter.Value);
        var enemies = fight is null || !_runtime.ShowEnemies.Value ? Array.Empty<MeterRow>() : MeterPresentation.Enemies(fight);
        var count = players.Length + enemies.Length;
        while (rows.Count < count) rows.Add(CreateRow(content, width, compact: compact));
        var pitch = compact ? HudRowPitch : RowPitch;
        var maximum = DamagePresentation.BarMaximum(players, enemies);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; row.Root.SetActive(i < count);
            if (i >= count) continue;
            row.Root.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -i * pitch);
            var enemy = i >= players.Length;
            var index = enemy ? i - players.Length : i;
            var item = enemy ? enemies[index] : players[index];
            row.Selection = fight is null || item.PlayerId == 0 && item.CreatureId.Length == 0 ? null
                : new(fight.Id, enemy ? DetailActorKind.Creature : DetailActorKind.Player, item.PlayerId, item.CreatureId);
            row.ActorName = item.Name; row.EncounterLabel = fight?.Label ?? "";
            row.Root.GetComponent<Button>().interactable = row.Selection is not null;
            row.Bar.rectTransform.sizeDelta = new Vector2(maximum > 0 ? width * (float)(item.Damage / maximum) : 0, compact ? 22 : 26);
            row.Bar.color = enemy ? new Color(0.49f, 0.33f, 0.12f, 0.8f) : item.Team switch
            {
                1 => new Color(0.55f, 0.18f, 0.22f, 0.8f), 2 => new Color(0.16f, 0.33f, 0.52f, 0.8f),
                3 => new Color(0.2f, 0.42f, 0.3f, 0.8f), 4 => new Color(0.44f, 0.24f, 0.5f, 0.8f),
                _ => new Color(0.48f, 0.2f, 0.23f, 0.8f)
            };
            row.Name.text = $"{index + 1}. {(item.Team > 0 ? $"[{item.Team}] " : "")}{item.Name}";
            row.Value.text = MeterPresentation.Values(item);
            // Reserve what the rendered values actually need; names get the
            // remaining space instead of a fixed narrow fraction of the row.
            var valueWidth = Mathf.Clamp(row.Value.GetPreferredValues(row.Value.text).x + 8, 90, width * 0.55f);
            row.Value.rectTransform.anchoredPosition = new Vector2(width - valueWidth - 4, compact ? -1 : -2);
            row.Value.rectTransform.sizeDelta = new Vector2(valueWidth, compact ? 22 : 25);
            row.Name.rectTransform.sizeDelta = new Vector2(width - valueWidth - 16, compact ? 22 : 25);
        }
        content.sizeDelta = new Vector2(width, Math.Max(1, count * pitch));
        RefreshScrollbars();
    }

    private EncounterSnapshot[] Encounters() => _runtime.State.Active.OrderByDescending(x => x.StartedAt)
        .Concat(_runtime.State.History).Where(x => _runtime.Modes[x.Kind].Value).ToArray();

    public void Refresh()
    {
        if (!IsAlive) return;
        var encounters = Encounters();
        var latest = encounters.FirstOrDefault();
        _meter.SetActive(_runtime.Visible.Value);
        RefreshHudControls();
        var background = _meterBackground.color;
        background.a = _runtime.BackgroundOpacity.Value;
        _meterBackground.color = background;
        _meterRect.localScale = Vector3.one * _runtime.Scale.Value;
        if (!_dragging) _meterRect.anchoredPosition = new Vector2(_runtime.OffsetX.Value, _runtime.OffsetY.Value);
        _meterDetail.text = latest is null ? _runtime.State.StatusMessage : $"{latest.Label} · {TimeSpan.FromSeconds(latest.DurationSeconds):mm\\:ss}";
        RenderRows(_meterRows, _meterContent, latest, HudListWidth, true);
        RefreshDetails();
        if (!ReviewOpen) return;
        var reviewParent = _review.transform.parent.Cast<RectTransform>();
        _review.transform.localScale = Vector3.one * Math.Min(1f, Math.Min(reviewParent.rect.width / (ReviewWidth + 32), reviewParent.rect.height / (ReviewHeight + 32)));
        var selected = encounters.FirstOrDefault(x => x.Id == _selectedEncounter) ?? latest;
        _reviewTitle.text = "CombatMeter";
        _reviewDetail.text = selected is null ? _runtime.State.StatusMessage
            : $"{selected.Label} · {selected.Players.Length} {(selected.Players.Length == 1 ? "player" : "players")} · {selected.DurationSeconds:N1}s";
        RenderRows(_reviewRows, _reviewContent, selected, ReviewListWidth);
        _enemyLabel.text = _runtime.ShowEnemies.Value ? "Enemies: shown" : "Enemies: hidden";
        _filterLabel.transform.parent.gameObject.SetActive(selected?.Kind == EncounterKind.Boss);
        _filterLabel.text = _runtime.Filter.Value == BossTargetFilter.BossOnly ? "Targets: boss only" : "Targets: boss + summons";
        _visibleLabel.text = _runtime.Visible.Value ? "HUD: shown" : "HUD: hidden";
        _lockLabel.text = _runtime.Locked.Value ? "Position: locked" : "Position: unlocked";
        while (_historyButtons.Count < encounters.Length)
        {
            var index = _historyButtons.Count;
            _historyButtons.Add(Button(_historyContent, "", 0, index * 36, HistoryWidth, 30, () => SelectHistory(index)));
        }
        _historyContent.sizeDelta = new Vector2(HistoryWidth, Math.Max(1, encounters.Length * 36 - 6));
        for (var i = 0; i < _historyButtons.Count; i++)
        {
            var entry = _historyButtons[i]; entry.Button.gameObject.SetActive(i < encounters.Length);
            if (i < encounters.Length)
            {
                entry.Text.text = $"{encounters[i].Label} · {encounters[i].DurationSeconds:N0}s";
                entry.Button.targetGraphic.color = encounters[i].Id == selected?.Id
                    ? SelectedButtonRed : ButtonRed;
            }
        }
        foreach (var mode in _modeButtons)
        {
            var allowed = _runtime.State.Policy?.EnabledModes.Contains(mode.Kind) == true;
            mode.Button.interactable = allowed;
            var name = mode.Kind == EncounterKind.OpenWorld ? "World" : mode.Kind.ToString();
            mode.Text.text = _runtime.State.Policy is null ? $"{name}: waiting"
                : allowed ? $"{name}: {(_runtime.Modes[mode.Kind].Value ? "shown" : "hidden")}" : $"{name}: server off";
        }
        RefreshScrollbars();
    }

    private void SelectHistory(int index)
    {
        var encounters = Encounters();
        if (index < encounters.Length) _selectedEncounter = encounters[index].Id;
        Refresh();
    }
    private void CycleFilter() { _runtime.Filter.Value = _runtime.Filter.Value == BossTargetFilter.BossOnly ? BossTargetFilter.BossAndSummons : BossTargetFilter.BossOnly; Refresh(); }
    private void ToggleLock() { _runtime.Locked.Value = !_runtime.Locked.Value; _dragging = false; Refresh(); }
    private void RefreshHudControls()
    {
        var show = _meter.activeInHierarchy && !ReviewOpen && !DetailsOpen && ContainsPointer(_meterRect);
        _openButton.gameObject.SetActive(show); _lockButton.gameObject.SetActive(show);
        _meterDetail.rectTransform.anchoredPosition = new Vector2(show ? 2 : -24, 0);
        _meterDetail.rectTransform.sizeDelta = new Vector2(show ? HudWidth - 68 : HudWidth - 16, 26);
        _lockIcon.sprite = _runtime.Locked.Value ? _icons.Locked : _icons.Unlocked;
        var normal = new Color(0.82f, 0.79f, 0.72f);
        var highlight = new Color(1f, 0.9f, 0.66f);
        _cogIcon.color = show && ContainsPointer(_openButton) ? highlight : normal;
        _lockIcon.color = _runtime.Locked.Value || show && ContainsPointer(_lockButton) ? highlight : normal;
    }
    private void OpenDetails(DetailSelection selection, string name, string label)
    {
        _pinned = selection; _detailTitle.text = name; _detailSubtitle.text = label;
        _details.SetActive(true); _details.transform.SetAsLastSibling(); _detailContent.anchoredPosition = Vector2.zero;
        _keyboardButton = -1; _dragging = false; _tooltip?.DisableTooltip(); _tooltipText = null;
        _runtime.Inspect(selection); RefreshDetails();
    }
    public void CloseDetails()
    {
        _pinned = null; _detailDragging = false; _pressedButton = null; _pressedActor = null;
        _draggedScroll = null;
        if (_details != null) _details.SetActive(false);
        _runtime.Inspect(null); _hoverKey = null; _keyboardButton = -1;
        _tooltipText = null;
    }
    public void RefreshDetails()
    {
        if (!DetailsOpen) return;
        var parent = _detailRect.parent.Cast<RectTransform>();
        _detailRect.localScale = Vector3.one * Math.Min(1f, Math.Min(parent.rect.width / (DetailWidth + 32), parent.rect.height / (DetailHeight + 32)));
        ClampDetailPosition();
        var data = _runtime.Details.Current;
        var rows = data is { Available: true } ? DamagePresentation.Sources(data, _runtime.Filter.Value) : Array.Empty<SourceRow>();
        _detailMessage.text = data is { Available: true } && rows.Length == 0 ? "No damage recorded for these targets" : _runtime.Details.Message;
        _detailMessage.gameObject.SetActive(rows.Length == 0);
        if (data is { Available: true })
        { _detailTitle.text = data.ActorName; _detailSubtitle.text = $"{data.EncounterLabel} · {data.DurationSeconds:N1}s"; }
        _detailFilter.Button.gameObject.SetActive(data is { Available: true, ActorKind: DetailActorKind.Player, Kind: EncounterKind.Boss });
        _detailFilter.Text.text = _runtime.Filter.Value == BossTargetFilter.BossOnly ? "Targets: boss only" : "Targets: boss + summons";
        while (_sourceRows.Count < rows.Length)
        {
            var row = CreateRow(_detailContent, SourceListWidth, false);
            var marker = Box("Source color", row.Root.transform, 0, 2, 3, 22).gameObject.AddComponent<Image>();
            marker.raycastTarget = false; row.SourceMarker = marker;
            var icon = Box("Source icon", row.Root.transform, 7, 2, 22, 22).gameObject.AddComponent<Image>();
            icon.raycastTarget = false; icon.preserveAspect = true; row.Icon = icon;
            row.Name.rectTransform.anchoredPosition = new Vector2(35, -2);
            row.Name.rectTransform.sizeDelta = new Vector2(SourceListWidth - 203, 25);
            row.Value.rectTransform.anchoredPosition = new Vector2(SourceListWidth - 158, -2);
            row.Value.rectTransform.sizeDelta = new Vector2(150, 25);
            _sourceRows.Add(row);
        }
        var max = rows.Select(x => x.Damage).DefaultIfEmpty().Max();
        for (var i = 0; i < _sourceRows.Count; i++)
        {
            var row = _sourceRows[i]; row.Root.SetActive(i < rows.Length);
            if (i >= rows.Length) continue;
            var item = rows[i]; var visual = _sourceVisuals.Get(item);
            row.Root.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -i * RowPitch);
            row.Bar.rectTransform.sizeDelta = new Vector2(max > 0 ? SourceListWidth * (float)(item.Damage / max) : 0, 26);
            row.Bar.color = new Color(visual.Color.r, visual.Color.g, visual.Color.b, 0.55f);
            row.SourceMarker!.color = visual.Color;
            row.Name.color = Color.Lerp(visual.Color, Color.white, 0.55f);
            row.Name.text = visual.Name; row.Icon!.sprite = visual.Icon; row.Icon.gameObject.SetActive(visual.Icon != null);
            row.Name.rectTransform.anchoredPosition = new Vector2(visual.Icon != null ? 35 : 9, -2);
            row.Name.rectTransform.sizeDelta = new Vector2(SourceListWidth - (visual.Icon != null ? 203 : 177), 25);
            row.Value.text = $"{item.Damage:N0} · {item.Share:P1}";
        }
        _detailContent.sizeDelta = new Vector2(SourceListWidth, Math.Max(1, rows.Length * RowPitch));
        RefreshScrollbars();
    }
    private void ClampDetailPosition()
    {
        var parent = _detailRect.parent.Cast<RectTransform>();
        var half = _detailRect.rect.size * (_detailRect.localScale.x / 2);
        var limit = new Vector2(Math.Max(0, parent.rect.width / 2 - half.x), Math.Max(0, parent.rect.height / 2 - half.y));
        var position = _detailRect.anchoredPosition;
        _detailRect.anchoredPosition = new Vector2(Mathf.Clamp(position.x, -limit.x, limit.x), Mathf.Clamp(position.y, -limit.y, limit.y));
    }
    public void ToggleReview()
    {
        if (ReviewOpen) { CloseReview(); return; }
        ReviewOpen = true; _review.SetActive(true); _review.transform.SetAsLastSibling();
        _keyboardButton = -1; _runtime.RequestHistory(); Refresh();
    }
    public void CloseReview() { CloseDetails(); ReviewOpen = false; _dragging = false; if (_review != null) _review.SetActive(false); _tooltip?.DisableTooltip(); _hovered = null; _hoverKey = null; }
    public void UpdateDrag()
    {
        RefreshHudControls();
        UpdateControls();
        if (_dismissPointerClick) { _tooltip?.DisableTooltip(); return; }
        UpdateTooltip();
        if (DetailsOpen)
        {
            if (Input.GetMouseButtonDown(0) && ContainsPointer(_detailHeader))
            { _detailDragging = true; _detailDragStart = Input.mousePosition; _detailInitial = _detailRect.anchoredPosition; }
            if (_detailDragging && Input.GetMouseButton(0))
            {
                var canvas = _details.GetComponentInParent<Canvas>();
                _detailRect.anchoredPosition = _detailInitial + ((Vector2)Input.mousePosition - _detailDragStart) / Math.Max(canvas.scaleFactor, 0.01f);
                ClampDetailPosition();
            }
            if (Input.GetMouseButtonUp(0)) _detailDragging = false;
            return;
        }
        if (ReviewOpen) { _dragging = false; return; }
        if (_runtime.Locked.Value || !_meter.activeInHierarchy) { _dragging = false; return; }
        if (Input.GetMouseButtonDown(0) && ContainsPointer(_header))
        { _dragging = true; _dragStart = Input.mousePosition; _initialPosition = _meterRect.anchoredPosition; }
        if (_dragging && Input.GetMouseButton(0))
        {
            var canvas = _meter.GetComponentInParent<Canvas>();
            var factor = canvas != null ? Math.Max(canvas.scaleFactor, 0.01f) : 1;
            var position = _initialPosition + ((Vector2)Input.mousePosition - _dragStart) / factor;
            var parent = _meterRect.parent.Cast<RectTransform>();
            position.x = Mathf.Clamp(position.x, -parent.rect.width + _meterRect.rect.width * _runtime.Scale.Value, 0);
            position.y = Mathf.Clamp(position.y, -parent.rect.height + _meterRect.rect.height * _runtime.Scale.Value, 0);
            _meterRect.anchoredPosition = position;
        }
        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            _runtime.OffsetX.Value = _meterRect.anchoredPosition.x; _runtime.OffsetY.Value = _meterRect.anchoredPosition.y;
        }
    }
    private bool InActiveLayer(Transform target) => DetailsOpen ? target.IsChildOf(_details.transform)
        : !ReviewOpen || target.IsChildOf(_review.transform);
    private bool ButtonAvailable(Button button) => button != null && button.gameObject.activeInHierarchy && button.interactable
        && InActiveLayer(button.transform);

    private void UpdateControls()
    {
        RefreshScrollbars();
        if (_dismissPointerClick)
        {
            // Keep the native input context through release as well as press.
            // Dismissal must not click a review row or trigger a combat action.
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) _dismissReleasedFrame = -1;
            else if (_dismissReleasedFrame < 0) _dismissReleasedFrame = Time.frameCount;
            else if (Time.frameCount > _dismissReleasedFrame) _dismissPointerClick = false;
            return;
        }
        if (DetailsOpen && !_detailDragging && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            && !ContainsPointer(_detailRect))
        {
            CloseDetails(); _dismissPointerClick = true; _dismissReleasedFrame = -1;
            return;
        }
        if (_draggedScroll is { } dragging)
        {
            if (Input.GetMouseButton(0) && dragging.Track != null && dragging.Track.gameObject.activeInHierarchy && InActiveLayer(dragging.Track))
            {
                var position = dragging.Content.anchoredPosition;
                position.y = Geometry(dragging).OffsetAt(TrackPointer(dragging.Track), _scrollGrab);
                dragging.Content.anchoredPosition = position;
            }
            else _draggedScroll = null;
            RefreshScrollbars();
            return; // A scrollbar release must never activate a row underneath.
        }
        if (Input.GetMouseButtonDown(0))
        {
            var hit = _scrolls.LastOrDefault(x => x.Track != null && x.Track.gameObject.activeInHierarchy
                && InActiveLayer(x.Track) && PointerVisible(x.Track));
            if (hit?.Track != null && hit.Thumb != null)
            {
                _pressedButton = null; _pressedActor = null; _draggedScroll = hit;
                var geometry = Geometry(hit);
                var pointer = TrackPointer(hit.Track);
                _scrollGrab = ContainsPointer(hit.Thumb) ? pointer - geometry.ThumbTop : geometry.ThumbHeight / 2;
                var position = hit.Content.anchoredPosition;
                position.y = geometry.OffsetAt(pointer, _scrollGrab); hit.Content.anchoredPosition = position;
                RefreshScrollbars();
                return;
            }
        }
        ScrollView? hovered = null;
        foreach (var view in _scrolls)
        {
            if (!view.Viewport.gameObject.activeInHierarchy || !InActiveLayer(view.Viewport)) continue;
            if (!ContainsPointer(view.Viewport) && (view.Track == null || !PointerVisible(view.Track))) continue;
            hovered = view;
            var position = view.Content.anchoredPosition;
            position.y = Mathf.Clamp(position.y - Input.mouseScrollDelta.y * view.Scroll.scrollSensitivity, 0, Geometry(view).Range);
            view.Content.anchoredPosition = position;
        }
        if (ReviewOpen || DetailsOpen)
        {
            var keyboardScroll = hovered ?? _scrolls.FirstOrDefault(x => x.Content == (DetailsOpen ? _detailContent : _historyContent));
            if (keyboardScroll != null)
            {
                var position = keyboardScroll.Content.anchoredPosition;
                if (Input.GetKeyDown(KeyCode.PageDown)) position.y += keyboardScroll.Viewport.rect.height;
                if (Input.GetKeyDown(KeyCode.PageUp)) position.y -= keyboardScroll.Viewport.rect.height;
                if (Input.GetKeyDown(KeyCode.Home)) position.y = 0;
                if (Input.GetKeyDown(KeyCode.End)) position.y = Geometry(keyboardScroll).Range;
                position.y = Mathf.Clamp(position.y, 0, Geometry(keyboardScroll).Range);
                keyboardScroll.Content.anchoredPosition = position;
            }
        }
        RefreshScrollbars();
        if (Input.GetMouseButtonDown(0))
        {
            _pressedButton = _buttons.LastOrDefault(x => ButtonAvailable(x.Button)
                && PointerVisible(x.Button.GetComponent<RectTransform>())).Button;
            var row = _meterRows.Concat(_reviewRows).FirstOrDefault(x => _pressedButton != null && x.Root == _pressedButton.gameObject);
            _pressedActor = row?.Selection is { } selection ? (selection, row.ActorName, row.EncounterLabel) : null;
        }
        if (Input.GetMouseButtonUp(0))
        {
            var pressed = _pressedButton; _pressedButton = null;
            var actor = _pressedActor; _pressedActor = null;
            if (pressed != null && ButtonAvailable(pressed) && PointerVisible(pressed.GetComponent<RectTransform>()))
            {
                if (actor is { } captured) OpenDetails(captured.Selection, captured.Name, captured.Label);
                else _buttons.First(x => x.Button == pressed).Action();
            }
        }
        if (!ReviewOpen && !DetailsOpen) return;
        var available = _buttons.Where(x => ButtonAvailable(x.Button)).ToArray();
        if (available.Length == 0) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            var backwards = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            _keyboardButton = _keyboardButton < 0 ? (backwards ? available.Length - 1 : 0)
                : (_keyboardButton + (backwards ? available.Length - 1 : 1)) % available.Length;
            available[_keyboardButton].Button.Select();
        }
        if (_keyboardButton >= 0 && _keyboardButton < available.Length && Input.GetKeyDown(KeyCode.Return))
            available[_keyboardButton].Action();
    }
    private void UpdateTooltip()
    {
        if (DetailsOpen) { _tooltip?.DisableTooltip(); _runtime.Inspect(_pinned); return; }
        var row = _meterRows.Concat(_reviewRows).FirstOrDefault(x => x.Selection is not null
            && InActiveLayer(x.Root.transform) && PointerVisible(x.Root.GetComponent<RectTransform>()));
        if (row?.Selection is { } selected && !_dragging)
        {
            var key = selected.ToString();
            if (_hoverKey != key) { _hoverKey = key; _hoverStart = Time.realtimeSinceStartupAsDouble; _tooltip?.DisableTooltip(); _tooltipText = null; _runtime.Inspect(null); }
            if (Time.realtimeSinceStartupAsDouble - _hoverStart < 0.25) return;
            _runtime.Inspect(selected);
            var data = _runtime.Details.Current;
            var text = _runtime.Details.Message;
            if (data is { Available: true })
            {
                var sources = DamagePresentation.Sources(data, _runtime.Filter.Value);
                text = sources.Length == 0 ? "No damage recorded for these targets" : string.Join("\n", sources.Take(5).Select(x =>
                {
                    var visual = _sourceVisuals.Get(x);
                    return $"<color=#{ColorUtility.ToHtmlStringRGB(visual.Color)}>{TooltipText(visual.Name)}</color>: {x.Damage:N0} · {x.Share:P1}";
                }));
                if (sources.Length > 5) text += $"\n+ {sources.Length - 5} more sources";
            }
            ShowTooltip(row.ActorName, row.EncounterLabel, text + "\nClick for full breakdown.");
            return;
        }
        _hoverKey = null; _runtime.Inspect(null);
        if (_tooltip == null) return;
        var hovered = _tooltips.FirstOrDefault(x => x.Target.gameObject.activeInHierarchy
            && InActiveLayer(x.Target) && PointerVisible(x.Target));
        _hovered = hovered.Target;
        if (_hovered == null) { _tooltip.DisableTooltip(); _tooltipText = null; return; }
        ShowTooltip(hovered.Title, "CombatMeter", hovered.Description);
    }
    private void ShowTooltip(string title, string subtitle, string text)
    {
        if (_tooltip == null) return;
        var content = title + "|" + subtitle + "|" + text;
        if (_tooltipText == content && (_tooltipPosition - Input.mousePosition).sqrMagnitude < 1) return;
        _tooltipText = content; _tooltipPosition = Input.mousePosition;
        _tooltip.SetTooltip(Input.mousePosition, TooltipText(title), TooltipText(subtitle), text, null);
    }
    private static string TooltipText(string text) => text.Replace("<", "‹").Replace(">", "›");
    public void Dispose()
    {
        CloseReview();
        if (_meter != null) Object.Destroy(_meter);
        if (_review != null) Object.Destroy(_review);
        if (_details != null) Object.Destroy(_details);
        if (_tooltip != null) Object.Destroy(_tooltip.gameObject);
        _icons.Dispose();
        if (_trimmedWindowSprite != null) Object.Destroy(_trimmedWindowSprite);
        foreach (var material in _outlinedFonts.Values) if (material != null) Object.Destroy(material);
        _outlinedFonts.Clear();
    }
}
