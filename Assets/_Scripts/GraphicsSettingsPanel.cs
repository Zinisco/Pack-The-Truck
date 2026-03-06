using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class GraphicsSettingsPanel : MonoBehaviour
{
    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown windowModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    private bool _isRefreshingUI;

    private void Awake()
    {
        GameSettings.Initialize();

        SetupWindowModes();
        SetupResolutions();
        RegisterListeners();
        RefreshUI();
    }

    private void OnEnable()
    {
        SetupResolutions();
        RefreshUI();
    }

    private void OnDestroy()
    {
        UnregisterListeners();
    }

    private void SetupWindowModes()
    {
        if (windowModeDropdown == null) return;

        windowModeDropdown.ClearOptions();
        windowModeDropdown.AddOptions(new List<string>
        {
            "Windowed",
            "Fullscreen",
            "Windowed Fullscreen"
        });
    }

    private void SetupResolutions()
    {
        if (resolutionDropdown == null) return;

        resolutionDropdown.ClearOptions();

        List<string> options = new List<string>();

        if (GraphicsSettingsApplier.Instance != null)
        {
            var resolutions = GraphicsSettingsApplier.Instance.UniqueResolutions;
            for (int i = 0; i < resolutions.Count; i++)
            {
                options.Add(resolutions[i].ToString());
            }
        }
        else
        {
            options.Add($"{Screen.width} x {Screen.height}");
        }

        resolutionDropdown.AddOptions(options);
    }

    private void RegisterListeners()
    {
        if (windowModeDropdown != null)
            windowModeDropdown.onValueChanged.AddListener(OnWindowModeChanged);

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    private void UnregisterListeners()
    {
        if (windowModeDropdown != null)
            windowModeDropdown.onValueChanged.RemoveListener(OnWindowModeChanged);

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
    }

    private void RefreshUI()
    {
        _isRefreshingUI = true;

        if (windowModeDropdown != null)
            windowModeDropdown.SetValueWithoutNotify((int)GameSettings.CurrentWindowMode);

        if (resolutionDropdown != null)
        {
            int maxIndex = Mathf.Max(0, resolutionDropdown.options.Count - 1);
            int clampedIndex = Mathf.Clamp(GameSettings.CurrentResolutionIndex, 0, maxIndex);
            resolutionDropdown.SetValueWithoutNotify(clampedIndex);
        }

        _isRefreshingUI = false;
    }

    private void OnWindowModeChanged(int index)
    {
        if (_isRefreshingUI) return;

        GameSettings.WindowMode mode = (GameSettings.WindowMode)index;
        GameSettings.SetWindowMode(mode);
    }

    private void OnResolutionChanged(int index)
    {
        if (_isRefreshingUI) return;

        GameSettings.SetResolutionIndex(index);
    }
}