using System;
using UnityEngine;

public static class GameSettings
{
    public const string MouseSensitivityKey = "MouseSensitivity";
    public const string ControllerSensitivityKey = "ControllerSensitivity";
    public const string InvertCameraXKey = "InvertCameraX";
    public const string InvertCameraYKey = "InvertCameraY";
    public const string WindowModeKey = "WindowMode";
    public const string ResolutionIndexKey = "ResolutionIndex";
    public const string MasterVolumeKey = "MasterVolume";
    public const string MusicVolumeKey = "MusicVolume";
    public const string SfxVolumeKey = "SfxVolume";

    public static event Action OnSettingsChanged;
    public static event Action<float> OnMouseSensitivityChanged;
    public static event Action<float> OnControllerSensitivityChanged;
    public static event Action<bool> OnInvertCameraXChanged;
    public static event Action<bool> OnInvertCameraYChanged;
    public static event Action<WindowMode> OnWindowModeChanged;
    public static event Action<int> OnResolutionIndexChanged;
    public static event Action<float> OnMasterVolumeChanged;
    public static event Action<float> OnMusicVolumeChanged;
    public static event Action<float> OnSfxVolumeChanged;

    private static bool _initialized;

    private static float _mouseSensitivity = 1f;
    private static float _controllerSensitivity = 1f;
    private static bool _invertCameraX;
    private static bool _invertCameraY;
    private static float _masterVolume = 0.8f;
    private static float _musicVolume = 0.8f;
    private static float _sfxVolume = 0.8f;

    public enum WindowMode
    {
        Windowed,
        Fullscreen,
        Borderless
    }

    private static WindowMode _windowMode = WindowMode.Windowed;
    private static int _resolutionIndex = 0;

    public static float MouseSensitivity
    {
        get
        {
            EnsureInitialized();
            return _mouseSensitivity;
        }
    }

    public static float ControllerSensitivity
    {
        get
        {
            EnsureInitialized();
            return _controllerSensitivity;
        }
    }

    public static bool InvertCameraX
    {
        get
        {
            EnsureInitialized();
            return _invertCameraX;
        }
    }

    public static bool InvertCameraY
    {
        get
        {
            EnsureInitialized();
            return _invertCameraY;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        Initialize();
    }

    public static void Initialize()
    {
        if (_initialized) return;

        _mouseSensitivity = PlayerPrefs.GetFloat(MouseSensitivityKey, 1f);
        _controllerSensitivity = PlayerPrefs.GetFloat(ControllerSensitivityKey, 1f);
        _invertCameraX = PlayerPrefs.GetInt(InvertCameraXKey, 0) == 1;
        _invertCameraY = PlayerPrefs.GetInt(InvertCameraYKey, 0) == 1;
        _masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 0.8f);
        _musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.8f);
        _sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 0.8f);

        int savedWindowMode = PlayerPrefs.GetInt(WindowModeKey, 0);
        savedWindowMode = Mathf.Clamp(savedWindowMode, 0, Enum.GetValues(typeof(WindowMode)).Length - 1);
        _windowMode = (WindowMode)savedWindowMode;

        _resolutionIndex = PlayerPrefs.GetInt(ResolutionIndexKey, 0);

        _initialized = true;
    }

    public static void EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
    }

    public static void SetMouseSensitivity(float value)
    {
        EnsureInitialized();

        if (Mathf.Approximately(_mouseSensitivity, value))
            return;

        _mouseSensitivity = value;

        // Update PlayerPrefs cache only, do not Save yet
        PlayerPrefs.SetFloat(MouseSensitivityKey, _mouseSensitivity);

        OnMouseSensitivityChanged?.Invoke(_mouseSensitivity);
        OnSettingsChanged?.Invoke();
    }

    public static void SetControllerSensitivity(float value)
    {
        EnsureInitialized();

        if (Mathf.Approximately(_controllerSensitivity, value))
            return;

        _controllerSensitivity = value;

        // Update PlayerPrefs cache only, do not Save yet
        PlayerPrefs.SetFloat(ControllerSensitivityKey, _controllerSensitivity);

        OnControllerSensitivityChanged?.Invoke(_controllerSensitivity);
        OnSettingsChanged?.Invoke();
    }

    public static void SetInvertCameraX(bool value)
    {
        EnsureInitialized();

        if (_invertCameraX == value)
            return;

        _invertCameraX = value;

        // Update PlayerPrefs cache only, do not Save yet
        PlayerPrefs.SetInt(InvertCameraXKey, _invertCameraX ? 1 : 0);

        OnInvertCameraXChanged?.Invoke(_invertCameraX);
        OnSettingsChanged?.Invoke();
    }

    public static void SetInvertCameraY(bool value)
    {
        EnsureInitialized();

        if (_invertCameraY == value)
            return;

        _invertCameraY = value;

        // Update PlayerPrefs cache only, do not Save yet
        PlayerPrefs.SetInt(InvertCameraYKey, _invertCameraY ? 1 : 0);

        OnInvertCameraYChanged?.Invoke(_invertCameraY);
        OnSettingsChanged?.Invoke();
    }

    public static WindowMode CurrentWindowMode
    {
        get
        {
            EnsureInitialized();
            return _windowMode;
        }
    }

    public static int CurrentResolutionIndex
    {
        get
        {
            EnsureInitialized();
            return _resolutionIndex;
        }
    }

    public static void SetWindowMode(WindowMode mode)
    {
        EnsureInitialized();

        if (_windowMode == mode)
            return;

        _windowMode = mode;
        PlayerPrefs.SetInt(WindowModeKey, (int)_windowMode);

        OnWindowModeChanged?.Invoke(_windowMode);
        OnSettingsChanged?.Invoke();
    }

    public static void SetResolutionIndex(int index)
    {
        EnsureInitialized();

        if (_resolutionIndex == index)
            return;

        _resolutionIndex = index;
        PlayerPrefs.SetInt(ResolutionIndexKey, _resolutionIndex);

        OnResolutionIndexChanged?.Invoke(_resolutionIndex);
        OnSettingsChanged?.Invoke();
    }

    public static float MasterVolume
    {
        get
        {
            EnsureInitialized();
            return _masterVolume;
        }
    }

    public static float MusicVolume
    {
        get
        {
            EnsureInitialized();
            return _musicVolume;
        }
    }

    public static float SfxVolume
    {
        get
        {
            EnsureInitialized();
            return _sfxVolume;
        }
    }

    public static void SetMasterVolume(float value)
    {
        EnsureInitialized();

        value = Mathf.Clamp01(value);

        if (Mathf.Approximately(_masterVolume, value))
            return;

        _masterVolume = value;
        PlayerPrefs.SetFloat(MasterVolumeKey, _masterVolume);

        OnMasterVolumeChanged?.Invoke(_masterVolume);
        OnSettingsChanged?.Invoke();
    }

    public static void SetMusicVolume(float value)
    {
        EnsureInitialized();

        value = Mathf.Clamp01(value);

        if (Mathf.Approximately(_musicVolume, value))
            return;

        _musicVolume = value;
        PlayerPrefs.SetFloat(MusicVolumeKey, _musicVolume);

        OnMusicVolumeChanged?.Invoke(_musicVolume);
        OnSettingsChanged?.Invoke();
    }

    public static void SetSfxVolume(float value)
    {
        EnsureInitialized();

        value = Mathf.Clamp01(value);

        if (Mathf.Approximately(_sfxVolume, value))
            return;

        _sfxVolume = value;
        PlayerPrefs.SetFloat(SfxVolumeKey, _sfxVolume);

        OnSfxVolumeChanged?.Invoke(_sfxVolume);
        OnSettingsChanged?.Invoke();
    }

    public static void SaveAll()
    {
        EnsureInitialized();

        PlayerPrefs.SetFloat(MouseSensitivityKey, _mouseSensitivity);
        PlayerPrefs.SetFloat(ControllerSensitivityKey, _controllerSensitivity);
        PlayerPrefs.SetInt(InvertCameraXKey, _invertCameraX ? 1 : 0);
        PlayerPrefs.SetInt(InvertCameraYKey, _invertCameraY ? 1 : 0);
        PlayerPrefs.SetInt(WindowModeKey, (int)_windowMode);
        PlayerPrefs.SetInt(ResolutionIndexKey, _resolutionIndex);
        PlayerPrefs.SetFloat(MasterVolumeKey, _masterVolume);
        PlayerPrefs.SetFloat(MusicVolumeKey, _musicVolume);
        PlayerPrefs.SetFloat(SfxVolumeKey, _sfxVolume);
        PlayerPrefs.Save();
    }
}