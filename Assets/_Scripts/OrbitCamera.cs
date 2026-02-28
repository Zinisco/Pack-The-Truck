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
    public float orbitSpeed = 0.12f;
    public float rotateSmooth = 12f;

    [Header("Zoom (distance)")]
    public float distance = 7f;
    public float minDistance = 4f;
    public float maxDistance = 22f;

    [Header("Button Zoom")]
    public float zoomStep = 1.2f;         // per press
    public float zoomRepeatDelay = 0.06f; // hold repeat
    public float zoomSmoothTime = 0.12f;

    [Header("Pan (LMB drag)")]
    public bool enablePan = true;
    public float panSpeed = 0.012f;
    public float panSmooth = 18f;
    public PlacementController placement;

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    float _targetYaw;
    float _targetPitch;

    float _targetDistance;
    float _zoomVel;

    Vector3 _panOffset;
    Vector3 _panOffsetTarget;

    float _nextZoomTime;

    void Awake()
    {
        _input = new InputSystem_Actions();
        _player = _input.Player;

        if (!orbitCam) orbitCam = Camera.main;

        _targetYaw = yaw;
        _targetPitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        _targetDistance = distance;

        if (orbitCam) orbitCam.orthographic = false;
    }

    void OnEnable() => _player.Enable();
    void OnDisable() => _player.Disable();

    void LateUpdate()
    {
        if (!pivot || !camRig) return;
        if (!orbitCam) orbitCam = Camera.main;
        if (!orbitCam) return;

        if (grid) grid.cameraTransform = orbitCam.transform;

        Vector3 basePivot = pivot.position;
        if (grid) basePivot = grid.GetWorldFloorCenter();

        // RMB orbit
        if (_player.OrbitHold.IsPressed())
        {
            Vector2 d = _player.OrbitDelta.ReadValue<Vector2>();
            _targetYaw += d.x * orbitSpeed;
            _targetPitch -= d.y * orbitSpeed;
            _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);
        }

        // LMB pan (disabled while placing)
        bool allowPan = enablePan;
        if (placement != null && placement.IsPlacing) allowPan = false;

        if (allowPan && _player.PanHold.IsPressed())
        {
            Vector2 d = _player.PanDelta.ReadValue<Vector2>();

            Vector3 right = orbitCam.transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(orbitCam.transform.forward, Vector3.up).normalized;

            _panOffsetTarget += (-right * d.x - forward * d.y) * panSpeed;
        }

        // Zoom with ZoomIn / ZoomOut buttons (no scroll)
        if (Time.unscaledTime >= _nextZoomTime)
        {
            bool zin = _player.ZoomIn.IsPressed() || _player.ZoomIn.WasPressedThisFrame();
            bool zout = _player.ZoomOut.IsPressed() || _player.ZoomOut.WasPressedThisFrame();

            if (zin || zout)
            {
                float dir = zin ? -1f : 1f;
                _targetDistance = Mathf.Clamp(_targetDistance + dir * zoomStep, minDistance, maxDistance);
                _nextZoomTime = Time.unscaledTime + zoomRepeatDelay;
            }
        }

        // Smooth orbit
        yaw = Mathf.LerpAngle(yaw, _targetYaw, Time.deltaTime * rotateSmooth);
        pitch = Mathf.Lerp(pitch, _targetPitch, Time.deltaTime * rotateSmooth);

        // Smooth pan + zoom
        _panOffset = Vector3.Lerp(_panOffset, _panOffsetTarget, Time.deltaTime * panSmooth);
        distance = Mathf.SmoothDamp(distance, _targetDistance, ref _zoomVel, zoomSmoothTime);

        Vector3 pivotWorld = basePivot + _panOffset;
        pivot.position = pivotWorld;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -distance);

        camRig.position = pivotWorld + offset;
        camRig.rotation = rot;
    }
}