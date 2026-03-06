using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Globalization;

public class GeneralSettingsPanel : MonoBehaviour
{
    [Header("Mouse Sensitivity")]
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private TMP_InputField mouseSensitivityInput;

    [Header("Controller Sensitivity")]
    [SerializeField] private Slider controllerSensitivitySlider;
    [SerializeField] private TMP_InputField controllerSensitivityInput;

    [Header("Invert Camera")]
    [SerializeField] private TMP_Dropdown invertCameraXDropdown; // No / Yes
    [SerializeField] private TMP_Dropdown invertCameraYDropdown; // No / Yes

    [Header("Ranges")]
    [SerializeField] private float minSensitivity = 0.1f;
    [SerializeField] private float maxSensitivity = 10f;

    [Header("Formatting")]
    [SerializeField] private int decimalPlaces = 2;

    private bool _isUpdatingUI;

    private void Awake()
    {
        GameSettings.Initialize();

        SetupSliders();
        SetupDropdowns();
        RegisterListeners();
        RefreshAllUIFromSettings();
    }

    private void OnDestroy()
    {
        UnregisterListeners();
    }

    private void OnEnable()
    {
        RefreshAllUIFromSettings();
    }

    private void SetupSliders()
    {
        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.minValue = minSensitivity;
            mouseSensitivitySlider.maxValue = maxSensitivity;
        }

        if (controllerSensitivitySlider != null)
        {
            controllerSensitivitySlider.minValue = minSensitivity;
            controllerSensitivitySlider.maxValue = maxSensitivity;
        }
    }

    private void SetupDropdowns()
    {
        SetupYesNoDropdown(invertCameraXDropdown);
        SetupYesNoDropdown(invertCameraYDropdown);
    }

    private void SetupYesNoDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null) return;

        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { "No", "Yes" });
    }

    private void RegisterListeners()
    {
        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSliderChanged);

        if (mouseSensitivityInput != null)
        {
            mouseSensitivityInput.onEndEdit.AddListener(OnMouseInputChanged);
            mouseSensitivityInput.onSubmit.AddListener(OnMouseInputChanged);
        }

        if (controllerSensitivitySlider != null)
            controllerSensitivitySlider.onValueChanged.AddListener(OnControllerSliderChanged);

        if (controllerSensitivityInput != null)
        {
            controllerSensitivityInput.onEndEdit.AddListener(OnControllerInputChanged);
            controllerSensitivityInput.onSubmit.AddListener(OnControllerInputChanged);
        }

        if (invertCameraXDropdown != null)
            invertCameraXDropdown.onValueChanged.AddListener(OnInvertXChanged);

        if (invertCameraYDropdown != null)
            invertCameraYDropdown.onValueChanged.AddListener(OnInvertYChanged);
    }

    private void UnregisterListeners()
    {
        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.onValueChanged.RemoveListener(OnMouseSliderChanged);

        if (mouseSensitivityInput != null)
        {
            mouseSensitivityInput.onEndEdit.RemoveListener(OnMouseInputChanged);
            mouseSensitivityInput.onSubmit.RemoveListener(OnMouseInputChanged);
        }

        if (controllerSensitivitySlider != null)
            controllerSensitivitySlider.onValueChanged.RemoveListener(OnControllerSliderChanged);

        if (controllerSensitivityInput != null)
        {
            controllerSensitivityInput.onEndEdit.RemoveListener(OnControllerInputChanged);
            controllerSensitivityInput.onSubmit.RemoveListener(OnControllerInputChanged);
        }

        if (invertCameraXDropdown != null)
            invertCameraXDropdown.onValueChanged.RemoveListener(OnInvertXChanged);

        if (invertCameraYDropdown != null)
            invertCameraYDropdown.onValueChanged.RemoveListener(OnInvertYChanged);
    }

    private void RefreshAllUIFromSettings()
    {
        _isUpdatingUI = true;

        float mouseSensitivity = Mathf.Clamp(GameSettings.MouseSensitivity, minSensitivity, maxSensitivity);
        float controllerSensitivity = Mathf.Clamp(GameSettings.ControllerSensitivity, minSensitivity, maxSensitivity);
        bool invertX = GameSettings.InvertCameraX;
        bool invertY = GameSettings.InvertCameraY;

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.SetValueWithoutNotify(mouseSensitivity);

        if (mouseSensitivityInput != null)
            mouseSensitivityInput.SetTextWithoutNotify(FormatFloat(mouseSensitivity));

        if (controllerSensitivitySlider != null)
            controllerSensitivitySlider.SetValueWithoutNotify(controllerSensitivity);

        if (controllerSensitivityInput != null)
            controllerSensitivityInput.SetTextWithoutNotify(FormatFloat(controllerSensitivity));

        if (invertCameraXDropdown != null)
            invertCameraXDropdown.SetValueWithoutNotify(invertX ? 1 : 0);

        if (invertCameraYDropdown != null)
            invertCameraYDropdown.SetValueWithoutNotify(invertY ? 1 : 0);

        _isUpdatingUI = false;
    }

    private string FormatFloat(float value)
    {
        return value.ToString("F" + decimalPlaces, CultureInfo.InvariantCulture);
    }

    private bool TryParseClamped(string text, out float result)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            result = Mathf.Clamp(result, minSensitivity, maxSensitivity);
            return true;
        }

        result = 0f;
        return false;
    }

    private void OnMouseSliderChanged(float value)
    {
        if (_isUpdatingUI) return;

        float clamped = Mathf.Clamp(value, minSensitivity, maxSensitivity);

        _isUpdatingUI = true;
        if (mouseSensitivityInput != null)
            mouseSensitivityInput.SetTextWithoutNotify(FormatFloat(clamped));
        _isUpdatingUI = false;

        GameSettings.SetMouseSensitivity(clamped);
    }

    private void OnMouseInputChanged(string text)
    {
        if (_isUpdatingUI) return;

        float finalValue = Mathf.Clamp(GameSettings.MouseSensitivity, minSensitivity, maxSensitivity);

        if (TryParseClamped(text, out float parsed))
            finalValue = parsed;

        _isUpdatingUI = true;

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.SetValueWithoutNotify(finalValue);

        if (mouseSensitivityInput != null)
            mouseSensitivityInput.SetTextWithoutNotify(FormatFloat(finalValue));

        _isUpdatingUI = false;

        GameSettings.SetMouseSensitivity(finalValue);
    }

    private void OnControllerSliderChanged(float value)
    {
        if (_isUpdatingUI) return;

        float clamped = Mathf.Clamp(value, minSensitivity, maxSensitivity);

        _isUpdatingUI = true;
        if (controllerSensitivityInput != null)
            controllerSensitivityInput.SetTextWithoutNotify(FormatFloat(clamped));
        _isUpdatingUI = false;

        GameSettings.SetControllerSensitivity(clamped);
    }

    private void OnControllerInputChanged(string text)
    {
        if (_isUpdatingUI) return;

        float finalValue = Mathf.Clamp(GameSettings.ControllerSensitivity, minSensitivity, maxSensitivity);

        if (TryParseClamped(text, out float parsed))
            finalValue = parsed;

        _isUpdatingUI = true;

        if (controllerSensitivitySlider != null)
            controllerSensitivitySlider.SetValueWithoutNotify(finalValue);

        if (controllerSensitivityInput != null)
            controllerSensitivityInput.SetTextWithoutNotify(FormatFloat(finalValue));

        _isUpdatingUI = false;

        GameSettings.SetControllerSensitivity(finalValue);
    }

    private void OnInvertXChanged(int index)
    {
        if (_isUpdatingUI) return;
        GameSettings.SetInvertCameraX(index == 1);
    }

    private void OnInvertYChanged(int index)
    {
        if (_isUpdatingUI) return;
        GameSettings.SetInvertCameraY(index == 1);
    }
}