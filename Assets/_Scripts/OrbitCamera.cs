using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    [Header("Refs")]
    public Transform pivot;          // look target / orbit center
    public Transform camRig;         // parent of the camera
    public Camera orbitCam;          // actual camera
    public GridManager grid;         // optional

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

    [Header("Scroll Zoom")]
    public float scrollZoomStep = 1.2f;
    public float scrollDeadZone = 0.01f;
    public float zoomSmoothTime = 0.12f;

    [Header("Pan (LMB drag)")]
    public bool enablePan = true;
    public float panSpeed = 0.012f;         // tuned for mouse delta
    public float panSmooth = 18f;           // bigger = snappier
    public bool panOnlyWhenPlacing = true;  // prevents interfering with pickup when not placing
    public PlacementController placement;   // assign (optional but recommended)

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    float _targetYaw;
    float _targetPitch;

    float _targetDistance;
    float _zoomVel;

    Vector3 _panOffset;         // world offset added to pivot position
    Vector3 _panOffsetTarget;

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

        // Base pivot position
        Vector3 basePivot = pivot.position;
        if (grid)
        {
            // Orbit around grid floor center (your current behavior)
            basePivot = grid.GetWorldFloorCenter();
        }

        // RMB orbit
        if (_player.OrbitHold.IsPressed())
        {
            Vector2 d = _player.OrbitDelta.ReadValue<Vector2>();
            _targetYaw += d.x * orbitSpeed;
            _targetPitch -= d.y * orbitSpeed;
            _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);
        }

        // LMB pan (use PanHold + PanDelta)
        bool allowPan = enablePan;
        if (allowPan && panOnlyWhenPlacing)
            allowPan = (placement != null && placement.IsPlacing);

        if (allowPan && _player.PanHold.IsPressed())
        {
            Vector2 d = _player.PanDelta.ReadValue<Vector2>();

            Vector3 right = orbitCam.transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(orbitCam.transform.forward, Vector3.up).normalized;

            _panOffsetTarget += (-right * d.x - forward * d.y) * panSpeed;
        }

        // Scroll zoom (use Zoom action)
        Vector2 scroll = _player.Zoom.ReadValue<Vector2>();

        // Unity scroll delta is usually in "pixels-ish" with 120 per notch on many mice.
        float notches = scroll.y / 120f;

        if (Mathf.Abs(notches) > scrollDeadZone)
        {
            _targetDistance = Mathf.Clamp(
                _targetDistance - notches * scrollZoomStep,
                minDistance,
                maxDistance
            );
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