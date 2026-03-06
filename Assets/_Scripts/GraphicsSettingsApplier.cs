using UnityEngine;
using System.Collections.Generic;

public class GraphicsSettingsApplier : MonoBehaviour
{
    [System.Serializable]
    public struct ResolutionOption
    {
        public int width;
        public int height;

        public ResolutionOption(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public override string ToString()
        {
            return $"{width} x {height}";
        }
    }

    public static GraphicsSettingsApplier Instance { get; private set; }

    private readonly List<ResolutionOption> _uniqueResolutions = new List<ResolutionOption>();

    public IReadOnlyList<ResolutionOption> UniqueResolutions => _uniqueResolutions;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        GameSettings.Initialize();
        BuildResolutionList();
        ClampSavedResolutionIndex();
        ApplyAllFromSettings();
    }

    void OnEnable()
    {
        GameSettings.OnWindowModeChanged += HandleWindowModeChanged;
        GameSettings.OnResolutionIndexChanged += HandleResolutionIndexChanged;
    }

    void OnDisable()
    {
        GameSettings.OnWindowModeChanged -= HandleWindowModeChanged;
        GameSettings.OnResolutionIndexChanged -= HandleResolutionIndexChanged;
    }

    public void BuildResolutionList()
    {
        _uniqueResolutions.Clear();

        Resolution[] systemResolutions = Screen.resolutions;
        HashSet<string> seen = new HashSet<string>();

        for (int i = 0; i < systemResolutions.Length; i++)
        {
            int width = systemResolutions[i].width;
            int height = systemResolutions[i].height;

            string key = width + "x" + height;
            if (seen.Contains(key))
                continue;

            seen.Add(key);
            _uniqueResolutions.Add(new ResolutionOption(width, height));
        }

        _uniqueResolutions.Sort((a, b) =>
        {
            int areaCompare = (a.width * a.height).CompareTo(b.width * b.height);
            if (areaCompare != 0) return areaCompare;

            int widthCompare = a.width.CompareTo(b.width);
            if (widthCompare != 0) return widthCompare;

            return a.height.CompareTo(b.height);
        });

        if (_uniqueResolutions.Count == 0)
        {
            _uniqueResolutions.Add(new ResolutionOption(Screen.width, Screen.height));
        }

        // If saved index is invalid, match current screen size.
        int savedIndex = GameSettings.CurrentResolutionIndex;
        if (savedIndex < 0 || savedIndex >= _uniqueResolutions.Count)
        {
            int currentIndex = FindMatchingCurrentResolutionIndex();
            GameSettings.SetResolutionIndex(currentIndex >= 0 ? currentIndex : 0);
        }
    }

    public void ApplyAllFromSettings()
    {
        ApplyWindowMode(GameSettings.CurrentWindowMode);

        int index = Mathf.Clamp(GameSettings.CurrentResolutionIndex, 0, _uniqueResolutions.Count - 1);
        ApplyResolution(index);
    }

    public void ApplyWindowMode(GameSettings.WindowMode mode)
    {
        switch (mode)
        {
            case GameSettings.WindowMode.Windowed:
                Screen.fullScreenMode = FullScreenMode.Windowed;
                break;

            case GameSettings.WindowMode.Fullscreen:
                Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                break;

            case GameSettings.WindowMode.Borderless:
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                break;
        }
    }

    public void ApplyResolution(int index)
    {
        if (_uniqueResolutions.Count == 0)
            return;

        index = Mathf.Clamp(index, 0, _uniqueResolutions.Count - 1);

        ResolutionOption res = _uniqueResolutions[index];
        Screen.SetResolution(res.width, res.height, Screen.fullScreenMode);
    }

    public int FindMatchingCurrentResolutionIndex()
    {
        int currentWidth = Screen.width;
        int currentHeight = Screen.height;

        for (int i = 0; i < _uniqueResolutions.Count; i++)
        {
            if (_uniqueResolutions[i].width == currentWidth &&
                _uniqueResolutions[i].height == currentHeight)
            {
                return i;
            }
        }

        int bestIndex = 0;
        int currentArea = currentWidth * currentHeight;
        int bestDiff = int.MaxValue;

        for (int i = 0; i < _uniqueResolutions.Count; i++)
        {
            int area = _uniqueResolutions[i].width * _uniqueResolutions[i].height;
            int diff = Mathf.Abs(area - currentArea);

            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void ClampSavedResolutionIndex()
    {
        if (_uniqueResolutions.Count == 0)
            return;

        int clamped = Mathf.Clamp(GameSettings.CurrentResolutionIndex, 0, _uniqueResolutions.Count - 1);
        if (clamped != GameSettings.CurrentResolutionIndex)
            GameSettings.SetResolutionIndex(clamped);
    }

    private void HandleWindowModeChanged(GameSettings.WindowMode mode)
    {
        ApplyWindowMode(mode);

        int clampedIndex = Mathf.Clamp(GameSettings.CurrentResolutionIndex, 0, _uniqueResolutions.Count - 1);
        ApplyResolution(clampedIndex);
    }

    private void HandleResolutionIndexChanged(int index)
    {
        ApplyResolution(index);
    }
}