using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCameraWithPresets : MonoBehaviour
{
    [Header("Refs")]
    public Transform pivot;
    public Transform camRig;
    public Camera orbitCam;          // assign your camera (child of camRig). Fallback: Camera.main
    public GridManager grid;

    [Header("Orbit")]
    public float yaw = 45f;
    public float pitch = 25f;
    public float pitchMin = -89f;
    public float pitchMax = 89f;
    public float orbitSpeed = 0.12f;

    [Header("Perspective Zoom (distance)")]
    public float distance = 7f;
    public float minDistance = 4f;
    public float maxDistance = 22f;

    [Header("Zoom Input")]
    [Tooltip("Units per second while holding zoom buttons")]
    public float zoomSpeed = 6f;

    [Tooltip("Smaller = snappier, larger = smoother")]
    public float zoomSmoothTime = 0.12f;

    [Header("Ortho Zoom (size)")]
    [Tooltip("Extra padding when fitting ortho to the grid")]
    public float orthoFitPadding = 1.08f;

    public float minOrthoSize = 0.5f;
    public float maxOrthoSize = 50f;

    [Header("Layer")]
    public int layerIndex = 0;

    [Header("Preset Smooth")]
    public float presetLerp = 10f;

    [Header("2D Exit")]
    public float orbitUnlockDeadZone = 0.001f;

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    float _targetYaw;
    float _targetPitch;

    float _targetDistance;
    float _zoomVel;           // SmoothDamp velocity for distance

    float _orthoSize;
    float _targetOrthoSize;
    float _orthoVel;          // SmoothDamp velocity for orthoSize

    void Awake()
    {
        _input = new InputSystem_Actions();
        _player = _input.Player;

        if (!orbitCam) orbitCam = Camera.main;

        _targetYaw = yaw;
        _targetPitch = pitch;

        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        _targetDistance = distance;

        if (orbitCam)
        {
            _orthoSize = orbitCam.orthographicSize;
            _targetOrthoSize = _orthoSize;
        }

        if (grid && orbitCam)
            grid.cameraTransform = orbitCam.transform;
    }

    void OnEnable() => _player.Enable();
    void OnDisable() => _player.Disable();

    bool Is2DMode(GridManager.GridViewMode m)
        => m != GridManager.GridViewMode.Perspective;

    void SetPreset(float y, float p)
    {
        _targetYaw = y;
        _targetPitch = Mathf.Clamp(p, pitchMin, pitchMax);

        // When we go into a 2D preset, update the ortho target to a “fit grid” size.
        if (grid && orbitCam && Is2DMode(grid.viewMode))
        {
            _targetOrthoSize = Mathf.Clamp(ComputeFitOrthoSize(grid.viewMode), minOrthoSize, maxOrthoSize);
        }
    }

    float ComputeFitOrthoSize(GridManager.GridViewMode mode)
    {
        if (!grid || !orbitCam) return orbitCam ? orbitCam.orthographicSize : 5f;

        float sx = grid.size.x * grid.cellSize;
        float sy = grid.size.y * grid.cellSize;
        float sz = grid.size.z * grid.cellSize;

        float aspect = Mathf.Max(0.0001f, orbitCam.aspect);

        // OrthoSize is half the vertical size visible in world units.
        // To fit a rectangle (width, height) on screen:
        // orthoSize >= height/2
        // orthoSize >= (width/2)/aspect
        float width, height;

        switch (mode)
        {
            case GridManager.GridViewMode.Top2D:
            case GridManager.GridViewMode.Bottom2D:
                width = sx; height = sz;  // looking down: XZ plane
                break;

            case GridManager.GridViewMode.Left2D:
            case GridManager.GridViewMode.Right2D:
                width = sz; height = sy;  // looking at YZ plane
                break;

            case GridManager.GridViewMode.Front2D:
            case GridManager.GridViewMode.Back2D:
                width = sx; height = sy;  // looking at XY plane
                break;

            default:
                width = sx; height = sz;
                break;
        }

        float fit = Mathf.Max(height * 0.5f, (width * 0.5f) / aspect);
        return fit * orthoFitPadding;
    }

    void ApplyCameraProjection()
    {
        if (!orbitCam || !grid) return;

        bool wantOrtho = Is2DMode(grid.viewMode);

        if (orbitCam.orthographic != wantOrtho)
        {
            orbitCam.orthographic = wantOrtho;

            // When entering ortho, choose a good starting size.
            if (wantOrtho)
            {
                _targetOrthoSize = Mathf.Clamp(ComputeFitOrthoSize(grid.viewMode), minOrthoSize, maxOrthoSize);
                _orthoSize = orbitCam.orthographicSize; // keep current as start
            }
        }
    }

    void LateUpdate()
    {
        if (!pivot || !camRig) return;
        if (!orbitCam) orbitCam = Camera.main;
        if (!orbitCam) return;

        // Ensure grid gets the right camera transform
        if (grid) grid.cameraTransform = orbitCam.transform;

        // RMB + mouse delta: if player actually moves -> exit 2D to Perspective
        if (_player.OrbitHold.IsPressed())
        {
            Vector2 d = _player.OrbitDelta.ReadValue<Vector2>();
            if (grid != null && d.sqrMagnitude > orbitUnlockDeadZone && grid.viewMode != GridManager.GridViewMode.Perspective)
                grid.SetViewMode(GridManager.GridViewMode.Perspective);

            _targetYaw += d.x * orbitSpeed;
            _targetPitch -= d.y * orbitSpeed;
            _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);
        }

        // Presets (set view mode first, then preset so fit-size uses correct mode)
        if (_player.ViewTop.WasPressedThisFrame()) { if (grid) grid.SetViewMode(GridManager.GridViewMode.Top2D); SetPreset(0f, 89f); }
        if (_player.ViewBottom.WasPressedThisFrame()) { if (grid) grid.SetViewMode(GridManager.GridViewMode.Bottom2D); SetPreset(0f, -89f); }

        if (_player.ViewLeft.WasPressedThisFrame()) { if (grid) grid.SetViewMode(GridManager.GridViewMode.Left2D); SetPreset(-90f, 0f); }
        if (_player.ViewRight.WasPressedThisFrame()) { if (grid) grid.SetViewMode(GridManager.GridViewMode.Right2D); SetPreset(90f, 0f); }
        if (_player.ViewFront.WasPressedThisFrame()) { if (grid) grid.SetViewMode(GridManager.GridViewMode.Front2D); SetPreset(0f, 0f); }
        if (_player.ViewBack.WasPressedThisFrame()) { if (grid) grid.SetViewMode(GridManager.GridViewMode.Back2D); SetPreset(180f, 0f); }

        // Switch camera projection based on mode
        ApplyCameraProjection();

        // HOLD-to-zoom (continuous)
        float zoomDir = 0f;
        if (_player.ZoomIn.IsPressed()) zoomDir -= 1f;
        if (_player.ZoomOut.IsPressed()) zoomDir += 1f;

        if (zoomDir != 0f)
        {
            if (grid && Is2DMode(grid.viewMode) && orbitCam.orthographic)
            {
                // Ortho zoom: smaller size = closer
                _targetOrthoSize = Mathf.Clamp(
                    _targetOrthoSize + zoomDir * zoomSpeed * Time.deltaTime,
                    minOrthoSize,
                    maxOrthoSize
                );
            }
            else
            {
                // Perspective zoom: smaller distance = closer
                _targetDistance = Mathf.Clamp(
                    _targetDistance + zoomDir * zoomSpeed * Time.deltaTime,
                    minDistance,
                    maxDistance
                );
            }
        }

        // Smooth rotate
        yaw = Mathf.LerpAngle(yaw, _targetYaw, Time.deltaTime * presetLerp);
        pitch = Mathf.Lerp(pitch, _targetPitch, Time.deltaTime * presetLerp);

        // Smooth zoom
        if (orbitCam.orthographic && grid && Is2DMode(grid.viewMode))
        {
            _orthoSize = Mathf.SmoothDamp(_orthoSize, _targetOrthoSize, ref _orthoVel, zoomSmoothTime);
            orbitCam.orthographicSize = _orthoSize;
        }
        else
        {
            distance = Mathf.SmoothDamp(distance, _targetDistance, ref _zoomVel, zoomSmoothTime);
        }

        // Pivot placement
        if (grid)
        {
            layerIndex = Mathf.Clamp(layerIndex, 0, grid.size.y - 1);

            switch (grid.viewMode)
            {
                case GridManager.GridViewMode.Top2D:
                    pivot.position = grid.GetWorldLayerCenter(layerIndex, useCellCenter: true);
                    break;
                case GridManager.GridViewMode.Bottom2D:
                    pivot.position = grid.GetWorldCeilingCenter();
                    break;
                case GridManager.GridViewMode.Left2D:
                    pivot.position = grid.GetWorldLeftWallCenter();
                    break;
                case GridManager.GridViewMode.Right2D:
                    pivot.position = grid.GetWorldRightWallCenter();
                    break;
                case GridManager.GridViewMode.Front2D:
                    pivot.position = grid.GetWorldFrontWallCenter();
                    break;
                case GridManager.GridViewMode.Back2D:
                    pivot.position = grid.GetWorldBackWallCenter();
                    break;
                default:
                    pivot.position = grid.GetWorldFloorCenter();
                    break;
            }
        }

        // Position + rotate rig
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        // Even in ortho we still place the rig by distance (it doesn’t affect scale, but affects what’s “in front”)
        float useDist = orbitCam.orthographic ? distance : distance;

        Vector3 offset = rot * new Vector3(0f, 0f, -useDist);
        camRig.position = pivot.position + offset;
        camRig.rotation = rot;
    }
}