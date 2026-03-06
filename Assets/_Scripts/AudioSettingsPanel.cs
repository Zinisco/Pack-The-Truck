using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;

public class AudioSettingsPanel : MonoBehaviour
{
    [Header("Master Volume")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private TMP_InputField masterVolumeInput;

    [Header("Music Volume")]
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TMP_InputField musicVolumeInput;

    [Header("SFX Volume")]
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TMP_InputField sfxVolumeInput;

    [Header("Ranges")]
    [SerializeField] private float minVolume = 0f;
    [SerializeField] private float maxVolume = 1f;

    private bool _isUpdatingUI;

    private void Awake()
    {
        GameSettings.Initialize();

        SetupSliders();
        RegisterListeners();
        RefreshAllUIFromSettings();
    }

    private void OnEnable()
    {
        RefreshAllUIFromSettings();
    }

    private void OnDestroy()
    {
        UnregisterListeners();
    }

    private void SetupSliders()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.minValue = minVolume;
            masterVolumeSlider.maxValue = maxVolume;
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.minValue = minVolume;
            musicVolumeSlider.maxValue = maxVolume;
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.minValue = minVolume;
            sfxVolumeSlider.maxValue = maxVolume;
        }
    }

    private void RegisterListeners()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterSliderChanged);

        if (masterVolumeInput != null)
        {
            masterVolumeInput.onEndEdit.AddListener(OnMasterInputChanged);
            masterVolumeInput.onSubmit.AddListener(OnMasterInputChanged);
        }

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        if (musicVolumeInput != null)
        {
            musicVolumeInput.onEndEdit.AddListener(OnMusicInputChanged);
            musicVolumeInput.onSubmit.AddListener(OnMusicInputChanged);
        }

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxSliderChanged);

        if (sfxVolumeInput != null)
        {
            sfxVolumeInput.onEndEdit.AddListener(OnSfxInputChanged);
            sfxVolumeInput.onSubmit.AddListener(OnSfxInputChanged);
        }
    }

    private void UnregisterListeners()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterSliderChanged);

        if (masterVolumeInput != null)
        {
            masterVolumeInput.onEndEdit.RemoveListener(OnMasterInputChanged);
            masterVolumeInput.onSubmit.RemoveListener(OnMasterInputChanged);
        }

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);

        if (musicVolumeInput != null)
        {
            musicVolumeInput.onEndEdit.RemoveListener(OnMusicInputChanged);
            musicVolumeInput.onSubmit.RemoveListener(OnMusicInputChanged);
        }

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxSliderChanged);

        if (sfxVolumeInput != null)
        {
            sfxVolumeInput.onEndEdit.RemoveListener(OnSfxInputChanged);
            sfxVolumeInput.onSubmit.RemoveListener(OnSfxInputChanged);
        }
    }

    private void RefreshAllUIFromSettings()
    {
        _isUpdatingUI = true;

        float master = Mathf.Clamp(GameSettings.MasterVolume, minVolume, maxVolume);
        float music = Mathf.Clamp(GameSettings.MusicVolume, minVolume, maxVolume);
        float sfx = Mathf.Clamp(GameSettings.SfxVolume, minVolume, maxVolume);

        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(master);

        if (masterVolumeInput != null)
            masterVolumeInput.SetTextWithoutNotify(FormatPercent(master));

        if (musicVolumeSlider != null)
            musicVolumeSlider.SetValueWithoutNotify(music);

        if (musicVolumeInput != null)
            musicVolumeInput.SetTextWithoutNotify(FormatPercent(music));

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.SetValueWithoutNotify(sfx);

        if (sfxVolumeInput != null)
            sfxVolumeInput.SetTextWithoutNotify(FormatPercent(sfx));

        _isUpdatingUI = false;
    }

    private string FormatPercent(float normalizedValue)
    {
        int percent = Mathf.RoundToInt(Mathf.Clamp01(normalizedValue) * 100f);
        return percent.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryParsePercent(string text, out float normalizedValue)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
        {
            percent = Mathf.Clamp(percent, 0f, 100f);
            normalizedValue = percent / 100f;
            normalizedValue = Mathf.Clamp(normalizedValue, minVolume, maxVolume);
            return true;
        }

        normalizedValue = 0f;
        return false;
    }

    private void OnMasterSliderChanged(float value)
    {
        if (_isUpdatingUI) return;

        float clamped = Mathf.Clamp(value, minVolume, maxVolume);

        _isUpdatingUI = true;
        if (masterVolumeInput != null)
            masterVolumeInput.SetTextWithoutNotify(FormatPercent(clamped));
        _isUpdatingUI = false;

        GameSettings.SetMasterVolume(clamped);
    }

    private void OnMasterInputChanged(string text)
    {
        if (_isUpdatingUI) return;

        float finalValue = Mathf.Clamp(GameSettings.MasterVolume, minVolume, maxVolume);

        if (TryParsePercent(text, out float parsed))
            finalValue = parsed;

        _isUpdatingUI = true;

        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(finalValue);

        if (masterVolumeInput != null)
            masterVolumeInput.SetTextWithoutNotify(FormatPercent(finalValue));

        _isUpdatingUI = false;

        GameSettings.SetMasterVolume(finalValue);
    }

    private void OnMusicSliderChanged(float value)
    {
        if (_isUpdatingUI) return;

        float clamped = Mathf.Clamp(value, minVolume, maxVolume);

        _isUpdatingUI = true;
        if (musicVolumeInput != null)
            musicVolumeInput.SetTextWithoutNotify(FormatPercent(clamped));
        _isUpdatingUI = false;

        GameSettings.SetMusicVolume(clamped);
    }

    private void OnMusicInputChanged(string text)
    {
        if (_isUpdatingUI) return;

        float finalValue = Mathf.Clamp(GameSettings.MusicVolume, minVolume, maxVolume);

        if (TryParsePercent(text, out float parsed))
            finalValue = parsed;

        _isUpdatingUI = true;

        if (musicVolumeSlider != null)
            musicVolumeSlider.SetValueWithoutNotify(finalValue);

        if (musicVolumeInput != null)
            musicVolumeInput.SetTextWithoutNotify(FormatPercent(finalValue));

        _isUpdatingUI = false;

        GameSettings.SetMusicVolume(finalValue);
    }

    private void OnSfxSliderChanged(float value)
    {
        if (_isUpdatingUI) return;

        float clamped = Mathf.Clamp(value, minVolume, maxVolume);

        _isUpdatingUI = true;
        if (sfxVolumeInput != null)
            sfxVolumeInput.SetTextWithoutNotify(FormatPercent(clamped));
        _isUpdatingUI = false;

        GameSettings.SetSfxVolume(clamped);
    }

    private void OnSfxInputChanged(string text)
    {
        if (_isUpdatingUI) return;

        float finalValue = Mathf.Clamp(GameSettings.SfxVolume, minVolume, maxVolume);

        if (TryParsePercent(text, out float parsed))
            finalValue = parsed;

        _isUpdatingUI = true;

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.SetValueWithoutNotify(finalValue);

        if (sfxVolumeInput != null)
            sfxVolumeInput.SetTextWithoutNotify(FormatPercent(finalValue));

        _isUpdatingUI = false;

        GameSettings.SetSfxVolume(finalValue);
    }
}