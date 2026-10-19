using BepInEx.Configuration;
using CombatMeter.Core;
using CombatMeter.Shared;
using ProjectM;
using ProjectM.UI;
using UnityEngine;

namespace CombatMeter.Client;

internal sealed class ClientRuntime : IDisposable
{
    private readonly Plugin _plugin;
    private readonly NativeTransport _transport;
    internal readonly MeterState State = new();
    internal readonly SourceDetailState Details = new();
    internal readonly ConfigEntry<bool> Visible;
    internal readonly ConfigEntry<bool> Locked;
    internal readonly ConfigEntry<float> Scale;
    internal readonly ConfigEntry<float> BackgroundOpacity;
    internal readonly ConfigEntry<float> OffsetX;
    internal readonly ConfigEntry<float> OffsetY;
    internal readonly ConfigEntry<KeyCode> ReviewKey;
    internal readonly ConfigEntry<bool> ShowEnemies;
    internal readonly ConfigEntry<BossTargetFilter> Filter;
    internal readonly Dictionary<EncounterKind, ConfigEntry<bool>> Modes = new();
    private NativeHud? _hud;
    private double _nextHudSearch;
    private double _nextRefresh;
    private double _nextRegistration;
    private bool _registered;
    private InputActionSystem? _focusedSystem;
    private ClientChatSystem.ChatFocusedInputContext? _nativeContext;
    private IInputContext? _inputContext;
    private bool _inputRegistered;
    private CursorLockMode _oldCursorLock;
    private bool _oldCursorVisible;
    private readonly List<UnityEngine.InputSystem.InputAction> _blockedScrollActions = new();
    internal bool ReviewOpen => _hud?.ReviewOpen == true;

    internal void SynchronizeInput(InputActionSystem system)
    {
        if (_focusedSystem != system) { ReleaseScrollInputs(); ReleaseInput(); _focusedSystem = system; }
        var wantsFocus = _hud?.WantsInputFocus == true;
        if (wantsFocus && !_inputRegistered)
        {
            _nativeContext = new ClientChatSystem.ChatFocusedInputContext();
            _inputContext = _nativeContext.Cast<IInputContext>();
            system.AddInputContext(_inputContext, _nativeContext, 100);
            _inputRegistered = true;
            _oldCursorLock = Cursor.lockState; _oldCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else if (!wantsFocus) ReleaseInput();
        SynchronizeScrollInputs(system);
    }

    private void SynchronizeScrollInputs(InputActionSystem system)
    {
        if (_hud?.CapturesWheel != true) { ReleaseScrollInputs(); return; }
        const AnalogInputAction scrollActions = AnalogInputAction.ZoomCamera | AnalogInputAction.MapZoom | AnalogInputAction.Menu_Scroll;
        var actions = system._AnalogInputActions;
        if (actions == null) return;
        foreach (var pair in actions)
        {
            if ((pair.Value & scrollActions) == 0 || !pair.Key.enabled) continue;
            // Leave Unity's raw mouse wheel untouched for our own list. Native
            // input actions are cancelled before camera/game contexts read them.
            if (!_blockedScrollActions.Contains(pair.Key)) _blockedScrollActions.Add(pair.Key);
            pair.Key.Disable();
        }
    }

    private void ReleaseScrollInputs()
    {
        foreach (var action in _blockedScrollActions)
            try { action.Enable(); }
            catch (Exception ex) { _plugin.Log.LogDebug($"CombatMeter scroll input was released: {ex.Message}"); }
        _blockedScrollActions.Clear();
    }

    private void ReleaseInput()
    {
        if (!_inputRegistered) return;
        try { if (_focusedSystem != null && _inputContext != null) _focusedSystem.RemoveInputContext(_inputContext); }
        catch (Exception ex) { _plugin.Log.LogDebug($"CombatMeter input world was released: {ex.Message}"); }
        _inputRegistered = false; _inputContext = null; _nativeContext = null;
        Cursor.lockState = _oldCursorLock; Cursor.visible = _oldCursorVisible;
    }

    public ClientRuntime(Plugin plugin)
    {
        _plugin = plugin;
        Visible = plugin.Config.Bind("Display", "Visible", true, "Show the live meter.");
        Locked = plugin.Config.Bind("Display", "PositionLocked", true, "Lock the position to disable dragging the meter header.");
        Scale = plugin.Config.Bind("Display", "Scale", 1f, new ConfigDescription("Meter scale.", new AcceptableValueRange<float>(0.6f, 2f)));
        BackgroundOpacity = plugin.Config.Bind("Display", "BackgroundOpacity", 0.9f,
            new ConfigDescription("DPS list background opacity. 0 is fully transparent; text, bars, icons, and menus keep their normal opacity.",
                new AcceptableValueRange<float>(0f, 1f)));
        OffsetX = plugin.Config.Bind("Display", "OffsetX", -8f, "Horizontal offset from the upper right HUD corner.");
        OffsetY = plugin.Config.Bind("Display", "OffsetY", -944f, "Vertical offset from the upper right HUD corner.");
        var layoutVersion = plugin.Config.Bind("Display", "LayoutVersion", 1, "Internal layout migration version.");
        if (layoutVersion.Value < 1)
        {
            if (OffsetX.Value == -24f && OffsetY.Value == -240f)
            { OffsetX.Value = -8f; OffsetY.Value = -944f; }
            layoutVersion.Value = 1;
        }
        ReviewKey = plugin.Config.Bind("Controls", "ReviewKey", KeyCode.F8, "Open/close review and settings. Escape closes it.");
        ShowEnemies = plugin.Config.Bind("Display", "ShowEnemies", true, "Show a separate list of monster and boss damage dealt to players.");
        Filter = plugin.Config.Bind("Display", "BossTargets", BossTargetFilter.BossOnly, "Count the boss alone or the boss and its summons.");
        foreach (var mode in Enum.GetValues<EncounterKind>())
            Modes[mode] = plugin.Config.Bind("Visibility", mode.ToString(), true, "Display this mode when server tracking is enabled.");
        _transport = new NativeTransport(OnPolicy, OnEncounter, OnSources, Ready);
    }

    private void Ready() { _registered = false; _nextRegistration = 0; }
    private void OnPolicy(PolicyPacket packet)
    {
        var previousSession = State.SessionId;
        _registered = State.Apply(packet);
        if (!_registered || previousSession != State.SessionId) { Details.Clear(); _hud?.CloseDetails(); }
        _plugin.Log.LogInfo($"[CombatMeter:Transport] policy received; accepted={_registered}; session={packet.ServerSessionId}");
        _hud?.Refresh();
    }
    private void OnEncounter(EncounterPacket packet)
    {
        if (State.Apply(packet)) _hud?.Refresh();
    }
    private void OnSources(SourceResponsePacket packet)
    {
        if (Details.Apply(packet)) _hud?.RefreshDetails();
    }
    internal void Inspect(DetailSelection? selection) => Details.Select(State.SessionId, selection, Time.realtimeSinceStartupAsDouble);
    internal void LogStyle(string text) => _plugin.Log.LogInfo("[CombatMeter:Style] " + text);

    internal void Update()
    {
        try
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (_transport.PollConnection(now)) _plugin.Log.LogInfo("[CombatMeter:Transport] native game connection ready");
            if (!_transport.IsReady && _registered) { _registered = false; State.Disconnect(); Details.Clear(); _hud?.CloseReview(); ReleaseScrollInputs(); ReleaseInput(); }
            if (_transport.IsReady && !_registered && now >= _nextRegistration)
            {
                _nextRegistration = now + 5;
                _transport.Register();
                _plugin.Log.LogInfo("[CombatMeter:Transport] registration sent");
            }
            if (_hud is not null && !_hud.IsAlive)
            {
                ReleaseScrollInputs(); ReleaseInput(); _hud.Dispose(); _hud = null;
                State.Disconnect(); Details.Clear(); _registered = false; _nextRegistration = 0;
            }
            if (_hud is null && now >= _nextHudSearch)
            {
                _nextHudSearch = now + 2;
                // This runs only during attachment, never every frame or per row.
                var canvas = UnityEngine.Object.FindObjectOfType<UICanvasBase>();
                if (canvas is not null && canvas.HUDParent is not null)
                {
                    _hud = new NativeHud(this, canvas);
                    _plugin.Log.LogInfo("CombatMeter attached to the native HUD canvas.");
                }
            }
            if (_hud is null) return;
            if (Input.GetKeyDown(ReviewKey.Value)) { _hud.CloseDetails(); _hud.ToggleReview(); }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_hud.DetailsOpen) _hud.CloseDetails(); else if (ReviewOpen) _hud.CloseReview();
            }
            _hud.UpdateDrag();
            if (_registered && _transport.IsReady && Details.Poll(now) is { } request) _transport.RequestSources(request);
            if (now >= _nextRefresh) { _nextRefresh = now + Protocol.SnapshotInterval; _hud.Refresh(); }
        }
        catch (Exception ex)
        {
            _plugin.Log.LogWarning($"CombatMeter client update failed: {ex.Message}");
            _nextHudSearch = Time.realtimeSinceStartupAsDouble + 5;
        }
    }

    internal void RequestHistory()
    {
        if (State.Policy is null || !_transport.IsReady) return;
        _transport.RequestHistory(State.SessionId);
    }

    public void Dispose()
    {
        ReleaseScrollInputs(); ReleaseInput(); _hud?.Dispose(); _hud = null; State.Disconnect(); Details.Clear();
        _transport.Dispose();
    }
}
