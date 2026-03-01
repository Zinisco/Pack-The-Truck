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

    [Tooltip("Mouse delta orbit speed")]
    public float mouseOrbitSpeed = 0.12f;

    [Tooltip("Right stick orbit speed")]
    public float stickOrbitSpeed = 120f;

    public float rotateSmooth = 12f;

    [Header("Distance (static for now)")]
    public float distance = 7f;

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    float _targetYaw;
    float _targetPitch;

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

        // --- ORBIT (Mouse RMB + delta) ---
        if (_player.OrbitHold.IsPressed())
        {
            Vector2 d = _player.OrbitDelta.ReadValue<Vector2>();
            _targetYaw += d.x * mouseOrbitSpeed;
            _targetPitch -= d.y * mouseOrbitSpeed;
        }

        // --- ORBIT (Gamepad Right Stick) ---
        Vector2 stick = _player.OrbitStick.ReadValue<Vector2>();
        if (stick.sqrMagnitude > 0.0001f)
        {
            _targetYaw += stick.x * stickOrbitSpeed * Time.unscaledDeltaTime;
            _targetPitch -= stick.y * stickOrbitSpeed * Time.unscaledDeltaTime;
        }

        _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);

        // Smooth orbit
        yaw = Mathf.LerpAngle(yaw, _targetYaw, Time.deltaTime * rotateSmooth);
        pitch = Mathf.Lerp(pitch, _targetPitch, Time.deltaTime * rotateSmooth);

        // Apply
        pivot.position = basePivot;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -distance);

        camRig.position = basePivot + offset;
        camRig.rotation = rot;
    }
}