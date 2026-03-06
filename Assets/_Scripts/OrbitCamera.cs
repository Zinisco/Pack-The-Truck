using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    [Header("Refs")]
    public Transform pivot;
    public Transform camRig;
    public Camera orbitCam;
    public GridManager grid;

    [Header("Orbit")]
    public float yaw = 45f;
    public float pitch = 25f;
    public float pitchMin = -80f;
    public float pitchMax = 80f;

    [Tooltip("Base mouse delta orbit speed")]
    public float mouseOrbitSpeed = 0.12f;

    [Tooltip("Base right stick orbit speed")]
    public float stickOrbitSpeed = 120f;

    public float rotateSmooth = 12f;

    [Header("Distance (static for now)")]
    public float distance = 7f;

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    float _targetYaw;
    float _targetPitch;

    float _mouseSensitivity = 1f;
    float _controllerSensitivity = 1f;
    bool _invertX;
    bool _invertY;

    void Awake()
    {
        GameSettings.Initialize();
        RefreshSettingsCache();
    }

    void OnEnable()
    {
        GameSettings.OnSettingsChanged += HandleSettingsChanged;
    }

    void OnDisable()
    {
        GameSettings.OnSettingsChanged -= HandleSettingsChanged;
    }

    void Start()
    {
        _input = InputHub.Instance.Actions;
        _player = _input.Player;

        if (!orbitCam) orbitCam = Camera.main;

        _targetYaw = yaw;
        _targetPitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

        if (orbitCam) orbitCam.orthographic = false;
    }

    void LateUpdate()
    {
        if (!pivot || !camRig) return;
        if (!orbitCam) orbitCam = Camera.main;
        if (!orbitCam) return;

        if (grid) grid.cameraTransform = orbitCam.transform;

        Vector3 basePivot = pivot.position;
        if (grid) basePivot = grid.GetWorldFloorCenter();

        float xInvert = _invertX ? -1f : 1f;
        float yInvert = _invertY ? -1f : 1f;

        // Mouse orbit
        if (_player.OrbitHold.IsPressed())
        {
            Vector2 d = _player.OrbitDelta.ReadValue<Vector2>();

            _targetYaw += d.x * mouseOrbitSpeed * _mouseSensitivity * xInvert;
            _targetPitch -= d.y * mouseOrbitSpeed * _mouseSensitivity * yInvert;
        }

        // Gamepad orbit
        Vector2 stick = _player.OrbitStick.ReadValue<Vector2>();
        if (stick.sqrMagnitude > 0.0001f)
        {
            _targetYaw += stick.x * stickOrbitSpeed * _controllerSensitivity * Time.unscaledDeltaTime * xInvert;
            _targetPitch -= stick.y * stickOrbitSpeed * _controllerSensitivity * Time.unscaledDeltaTime * yInvert;
        }

        _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);

        yaw = Mathf.LerpAngle(yaw, _targetYaw, Time.deltaTime * rotateSmooth);
        pitch = Mathf.Lerp(pitch, _targetPitch, Time.deltaTime * rotateSmooth);

        pivot.position = basePivot;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -distance);

        camRig.position = basePivot + offset;
        camRig.rotation = rot;
    }

    private void HandleSettingsChanged()
    {
        RefreshSettingsCache();
    }

    private void RefreshSettingsCache()
    {
        _mouseSensitivity = GameSettings.MouseSensitivity;
        _controllerSensitivity = GameSettings.ControllerSensitivity;
        _invertX = GameSettings.InvertCameraX;
        _invertY = GameSettings.InvertCameraY;
    }
}