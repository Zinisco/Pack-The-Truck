using UnityEngine;
using UnityEngine.Audio;

public class AudioSettingsApplier : MonoBehaviour
{
    public static AudioSettingsApplier Instance { get; private set; }

    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Exposed Parameter Names")]
    [SerializeField] private string masterVolumeParameter = "MasterVolume";
    [SerializeField] private string musicVolumeParameter = "MusicVolume";
    [SerializeField] private string sfxVolumeParameter = "SFXVolume";

    [Header("dB Range")]
    [SerializeField] private float minDb = -80f;
    [SerializeField] private float maxDb = 0f;

    [Header("SFX Preview")]
    [SerializeField] private AudioSource previewSfxSource;
    [SerializeField] private AudioClip sfxPreviewClip;
    [SerializeField] private float previewCooldown = 0.08f;
    [SerializeField] private bool previewOnMasterChange = false;
    [SerializeField] private bool previewOnSfxChange = true;

    private float _lastPreviewTime = -999f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        GameSettings.Initialize();
        ApplyAllFromSettings();
    }

    private void OnEnable()
    {
        GameSettings.OnMasterVolumeChanged += HandleMasterChanged;
        GameSettings.OnMusicVolumeChanged += HandleMusicChanged;
        GameSettings.OnSfxVolumeChanged += HandleSfxChanged;
    }

    private void OnDisable()
    {
        GameSettings.OnMasterVolumeChanged -= HandleMasterChanged;
        GameSettings.OnMusicVolumeChanged -= HandleMusicChanged;
        GameSettings.OnSfxVolumeChanged -= HandleSfxChanged;
    }

    public void ApplyAllFromSettings()
    {
        ApplyMasterVolume(GameSettings.MasterVolume);
        ApplyMusicVolume(GameSettings.MusicVolume);
        ApplySfxVolume(GameSettings.SfxVolume);
    }

    public void PlaySfxPreview()
    {
        if (previewSfxSource == null || sfxPreviewClip == null)
            return;

        if (Time.unscaledTime - _lastPreviewTime < previewCooldown)
            return;

        _lastPreviewTime = Time.unscaledTime;
        previewSfxSource.PlayOneShot(sfxPreviewClip);
    }

    private void HandleMasterChanged(float value)
    {
        ApplyMasterVolume(value);

        if (previewOnMasterChange)
            PlaySfxPreview();
    }

    private void HandleMusicChanged(float value)
    {
        ApplyMusicVolume(value);
    }

    private void HandleSfxChanged(float value)
    {
        ApplySfxVolume(value);

        if (previewOnSfxChange)
            PlaySfxPreview();
    }

    private void ApplyMasterVolume(float normalizedValue)
    {
        SetMixerVolume(masterVolumeParameter, normalizedValue);
    }

    private void ApplyMusicVolume(float normalizedValue)
    {
        SetMixerVolume(musicVolumeParameter, normalizedValue);
    }

    private void ApplySfxVolume(float normalizedValue)
    {
        SetMixerVolume(sfxVolumeParameter, normalizedValue);
    }

    private void SetMixerVolume(string parameterName, float normalizedValue)
    {
        if (audioMixer == null || string.IsNullOrWhiteSpace(parameterName))
            return;

        normalizedValue = Mathf.Clamp01(normalizedValue);
        float db = NormalizedToDb(normalizedValue);
        audioMixer.SetFloat(parameterName, db);
    }

    private float NormalizedToDb(float normalizedValue)
    {
        if (normalizedValue <= 0.0001f)
            return minDb;

        float db = Mathf.Log10(normalizedValue) * 20f;
        return Mathf.Clamp(db, minDb, maxDb);
    }
}