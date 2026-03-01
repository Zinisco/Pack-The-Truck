using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

public enum ControlSchemeMode { Auto, KeyboardMouse, Gamepad }

public class InputHub : MonoBehaviour
{
    public static InputHub Instance { get; private set; }

    public InputSystem_Actions Actions { get; private set; }

    [Header("Mode")]
    public ControlSchemeMode mode = ControlSchemeMode.Auto;

    [Header("Auto-switch")]
    [Tooltip("In Auto mode, how much mouse movement counts as 'touching' the mouse.")]
    public float mouseMoveThreshold = 0.5f;

    public ControlSchemeMode ActiveScheme { get; private set; } = ControlSchemeMode.KeyboardMouse;

    // NEW: keep the subscription so we can clean it up
    IDisposable _anyButtonPressSub;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        Actions = new InputSystem_Actions();
        Actions.Enable();

        ApplyMode(mode);
    }

    void OnEnable()
    {
        // FIX: onAnyButtonPress is an IObservable in some Input System versions
        _anyButtonPressSub = InputSystem.onAnyButtonPress
            .Call(OnAnyButtonPress);

        // Mouse movement isn’t a button press, so optionally detect it too.
        InputSystem.onEvent += OnInputEvent;
    }

    void OnDisable()
    {
        // FIX: dispose instead of -=
        _anyButtonPressSub?.Dispose();
        _anyButtonPressSub = null;

        InputSystem.onEvent -= OnInputEvent;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Actions?.Dispose();
    }

    public void ApplyMode(ControlSchemeMode newMode)
    {
        mode = newMode;

        if (mode == ControlSchemeMode.Auto)
        {
            if (Gamepad.current != null) SetActiveScheme(ControlSchemeMode.Gamepad);
            else SetActiveScheme(ControlSchemeMode.KeyboardMouse);
            return;
        }

        SetActiveScheme(mode);
    }

    void SetActiveScheme(ControlSchemeMode scheme)
    {
        if (scheme != ControlSchemeMode.KeyboardMouse && scheme != ControlSchemeMode.Gamepad)
            scheme = ControlSchemeMode.KeyboardMouse;

        ActiveScheme = scheme;

        string group = (scheme == ControlSchemeMode.Gamepad) ? "Gamepad" : "Keyboard&Mouse";
        Actions.bindingMask = InputBinding.MaskByGroup(group);

        if (mode == ControlSchemeMode.Auto)
        {
            Actions.devices = null;
            return;
        }

        if (scheme == ControlSchemeMode.Gamepad)
        {
            var gp = Gamepad.current;
            Actions.devices = (gp != null)
                ? new ReadOnlyArray<InputDevice>(new InputDevice[] { gp })
                : null;
        }
        else
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;

            if (kb != null && ms != null)
                Actions.devices = new ReadOnlyArray<InputDevice>(new InputDevice[] { kb, ms });
            else if (kb != null)
                Actions.devices = new ReadOnlyArray<InputDevice>(new InputDevice[] { kb });
            else if (ms != null)
                Actions.devices = new ReadOnlyArray<InputDevice>(new InputDevice[] { ms });
            else
                Actions.devices = null;
        }
    }

    void OnAnyButtonPress(InputControl control)
    {
        if (mode != ControlSchemeMode.Auto) return;

        var device = control.device;
        if (device == null) return;

        if (device is Gamepad)
        {
            if (ActiveScheme != ControlSchemeMode.Gamepad)
                SetActiveScheme(ControlSchemeMode.Gamepad);
        }
        else if (device is Keyboard || device is Mouse)
        {
            if (ActiveScheme != ControlSchemeMode.KeyboardMouse)
                SetActiveScheme(ControlSchemeMode.KeyboardMouse);
        }
    }

    void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (mode != ControlSchemeMode.Auto) return;
        if (device == null) return;

        if (device is Mouse m)
        {
            var d = m.delta.ReadValue();
            if (d.sqrMagnitude >= mouseMoveThreshold * mouseMoveThreshold)
            {
                if (ActiveScheme != ControlSchemeMode.KeyboardMouse)
                    SetActiveScheme(ControlSchemeMode.KeyboardMouse);
            }
        }
    }
}