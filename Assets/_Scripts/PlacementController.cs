using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementController : MonoBehaviour
{
    public enum PlacementMoveMode { Mouse, GridStep }

    [Header("Refs")]
    public Camera cam;
    public GridManager grid;

    [Header("Piece Selection")]
    public PieceDefinition currentDef;

    [Header("Movement")]
    public float keyRepeatDelay = 0.18f;

    [Header("Placement Move Mode")]
    public PlacementMoveMode moveMode = PlacementMoveMode.Mouse;

    [Header("Layer")]
    [Range(0, 20)] public int activeLayerY = 0;
    [SerializeField] float scrollLayerCooldown = 0.03f; // prevents “free spin” scrolling
    float _nextScrollTime = 0f;

    [Header("Layer Step (Gamepad)")]
    public float layerRepeatDelay = 0.20f;
    float _nextLayerStepTime = 0f;

    [Header("Ghost Visuals")]
    public Material validMat;
    public Material invalidMat;

    [Header("Pickup")]
    public LayerMask pickupMask;
    public float pickupMaxDistance = 100f;

    [Header("Place Feedback")]
    public AudioSource sfxSource;
    public AudioClip placeClip;
    [Range(0f, 1f)] public float placeVolume = 0.9f;
    public Vector2 placePitchRange = new Vector2(0.95f, 1.05f);

    [Header("Pop Animation")]
    public float popDuration = 0.12f;
    public float popUpScale = 1.08f;
    public float popDownScale = 0.98f;

    [Header("Support Debug")]
    public bool debugDrawSupport = true;
    public bool debugDrawUnsupported = true;
    public float debugLineHeight = 0.35f;

    [Header("Gamepad Selection (Cycle)")]
    public bool enableCycleSelect = true;
    public float selectionRefreshInterval = 0.35f;   // how often to rebuild list automatically
    public float maxSelectDistance = 100f;           // ignore super far pieces (optional)

    List<PickupPiece> _targets = new();
    int _targetIndex = -1;
    float _nextRefreshTime;

    PickupPiece _selected;

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    bool _isPlacing = false;
    public bool IsPlacing => _isPlacing;

    PickupPiece _heldPickup;

    GameObject _ghost;
    Renderer[] _ghostRenderers;

    Quaternion _rot = Quaternion.identity;
    Vector3Int _anchorCell;

    Vector3 _ghostVisualCenterLocal;
    Vector3 _heldVisualCenterLocal;

    bool _holdingExistingPlaced;
    string _placementWarning;

    float _nextMoveTime = 0f;

    int _nextPlacedId = 1;
    readonly List<Vector3Int> _tmpWorldCells = new();

    // --- Cancel Revert Cache (when picking up an already-placed piece) ---
    bool _restoreOnCancel = false;
    int _restoreId = 0;
    Vector3 _restorePos;
    Quaternion _restoreRot;
    Vector3 _restoreScale;
    bool _restoreWasActive;
    bool _subscribed;
    readonly List<Vector3Int> _restoreCells = new();

    bool TryEnsureInput()
    {
        if (_input != null) return true;

        if (InputHub.Instance == null || InputHub.Instance.Actions == null)
            return false;

        _input = InputHub.Instance.Actions;
        _player = _input.Player;
        return true;
    }

    void OnEnable()
    {
        _subscribed = false; // allow re-bind if this component is toggled on/off
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Subscribe()
    {
        if (_subscribed) return;

        _player.Click.performed += OnClick;
        _player.CancelPlacement.performed += OnCancelPlacement;
        _player.ConfirmPlacement.performed += OnConfirmPlacement;
        _player.LayerUp.performed += OnLayerUp;
        _player.LayerDown.performed += OnLayerDown;
        _player.LayerScroll.performed += OnLayerScroll;
        _player.CyclePrev.performed += OnCyclePrev;
        _player.CycleNext.performed += OnCycleNext;

        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        _player.Click.performed -= OnClick;
        _player.CancelPlacement.performed -= OnCancelPlacement;
        _player.ConfirmPlacement.performed -= OnConfirmPlacement;
        _player.LayerUp.performed -= OnLayerUp;
        _player.LayerDown.performed -= OnLayerDown;
        _player.LayerScroll.performed -= OnLayerScroll;
        _player.CyclePrev.performed -= OnCyclePrev;
        _player.CycleNext.performed -= OnCycleNext;

        _subscribed = false;
    }

    void Update()
    {
        if (_input == null)
        {
            if (!TryEnsureInput()) return;
        }
        if (!_subscribed) Subscribe();

        if (!cam || !grid) return;

        // --- Cycle selection when NOT placing ---
        if (!_isPlacing && enableCycleSelect && Time.time >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.time + selectionRefreshInterval;
            RefreshTargets();
        }

        // Now only proceed with placement logic if placing
        if (!_isPlacing) return;
        if (!currentDef) return;

        bool usingGamepad = (InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad);

        // Treat tiny stick noise as zero
        bool isOrbiting =
            _player.OrbitHold.IsPressed() ||
            _player.OrbitStick.ReadValue<Vector2>().sqrMagnitude > (0.2f * 0.2f);

        if (usingGamepad)
        {
            // Gamepad placement = grid step move (left stick)
            Vector2 move = _player.PieceMove.ReadValue<Vector2>();
            if (move.sqrMagnitude < 0.15f * 0.15f) move = Vector2.zero;
            StepMoveXZ(move);
        }
        else
        {
            // Mouse placement = follow mouse, BUT freeze while orbiting
            if (!isOrbiting)
                UpdateAnchorFromMouse();
        }

        // Rotate (keyboard only)
        HandleRotationInputActions();

        // Snap yaw
        if (_player.RotateYawPlus.WasPressedThisFrame())
        {
            RotateYaw(+90f);
        }

        if (_player.RotateYawMinus.WasPressedThisFrame())
        {
            RotateYaw(-90f);
        }

        // --- VALIDATION ---
        bool computed = ComputeWorldCells(currentDef, _anchorCell, _rot, _tmpWorldCells);
        bool hasSpace = computed && grid.CanPlaceCells(_tmpWorldCells);

        bool hasSupport = false;
        Vector3Int supportedCell = default, supportBelow = default;
        if (computed) hasSupport = HasAnySupportBelow(_tmpWorldCells, out supportedCell, out supportBelow);

        bool fragileOk = computed && PassesFragileTopRule(currentDef, _tmpWorldCells);
        bool standingOk = !currentDef.mustBeStanding || (computed && IsStandingFootprint(_tmpWorldCells));
        bool uprightOk = PassesUprightRule(currentDef, _rot);

        bool canPlace = computed && hasSpace && hasSupport && fragileOk && standingOk && uprightOk;

        _placementWarning = null;
        if (!computed) _placementWarning = "Invalid shape / rotation.";
        else if (!hasSpace) _placementWarning = "Blocked: space is occupied.";
        else if (!hasSupport) _placementWarning = "Needs support below.";
        else if (!fragileOk) _placementWarning = "Fragile: nothing can be directly on top.";
        else if (!standingOk) _placementWarning = "Must be standing upright.";
        else if (!uprightOk) _placementWarning = "Can't place upside down.";

        if (computed && debugDrawSupport)
            DebugDrawSupport(_tmpWorldCells, hasSupport, supportedCell, supportBelow);

        DebugDrawCells(_tmpWorldCells);
        UpdateGhostTransform();
        SetGhostMaterial(canPlace ? validMat : invalidMat);
    }

    void SetLayer(int y)
    {
        activeLayerY = Mathf.Clamp(y, 0, grid.size.y - 1);
        _anchorCell.y = activeLayerY;
    }

    void OnLayerUp(InputAction.CallbackContext _)
    {
        if (!_isPlacing) return;
        if (Time.time < _nextLayerStepTime) return;
        _nextLayerStepTime = Time.time + layerRepeatDelay;

        SetLayer(activeLayerY + 1);
    }

    void OnLayerDown(InputAction.CallbackContext _)
    {
        if (!_isPlacing) return;
        if (Time.time < _nextLayerStepTime) return;
        _nextLayerStepTime = Time.time + layerRepeatDelay;

        SetLayer(activeLayerY - 1);
    }

    void OnCyclePrev(InputAction.CallbackContext _)
    {
        if (_isPlacing) return;
        if (!enableCycleSelect) return;
        Cycle(-1);
    }

    void OnCycleNext(InputAction.CallbackContext _)
    {
        if (_isPlacing) return;
        if (!enableCycleSelect) return;
        Cycle(+1);
    }

    void RotateYaw(float delta)
    {
        // Yaw in *grid-local* space (same space as _rot)
        _rot = Quaternion.AngleAxis(delta, Vector3.up) * _rot;
        _rot = Normalize(_rot);
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
    }

    void HandleRotationKeys()
    {
        if (Keyboard.current == null) return;

        bool shift =
            Keyboard.current.leftShiftKey.isPressed ||
            Keyboard.current.rightShiftKey.isPressed;

        float degrees = shift ? -90f : 90f;

        // One tap = one 90° step (Shift reverses direction)
        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            RotateLocal(Vector3.right, degrees);
        }
        else if (Keyboard.current.yKey.wasPressedThisFrame)
        {
            RotateLocal(Vector3.up, degrees);
        }
        else if (Keyboard.current.zKey.wasPressedThisFrame)
        {
            RotateLocal(Vector3.forward, degrees);
        }
    }

    void RotateLocal(Vector3 axis, float degrees)
    {
        _rot = Quaternion.AngleAxis(degrees, axis) * _rot;
        _rot = Normalize(_rot);
    }

    void HandleRotationInputActions()
    {
        if (!_isPlacing || currentDef == null) return;

        bool usingGamepad = (InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad);

        bool negative = _player.RotationModifier.IsPressed();
        float degrees = negative ? -90f : 90f;

        if (_player.RotateX.WasPressedThisFrame())
            RotateLocal(Vector3.right, degrees);

        // Only allow RotateY from keyboard/mouse scheme
        if (!usingGamepad && _player.RotateY.WasPressedThisFrame())
            RotateLocal(Vector3.up, degrees);

        if (_player.RotateZ.WasPressedThisFrame())
            RotateLocal(Vector3.forward, degrees);
    }

    void StepMoveXZ(Vector2 move)
    {
        if (move == Vector2.zero) return;
        if (Time.time < _nextMoveTime) return;

        _nextMoveTime = Time.time + keyRepeatDelay;

        // --- Build camera-relative axes in GRID-LOCAL space (XZ only) ---
        Quaternion worldToGrid = Quaternion.Inverse(grid.origin.rotation);

        Vector3 camFwdLocal = worldToGrid * cam.transform.forward;
        Vector3 camRightLocal = worldToGrid * cam.transform.right;

        camFwdLocal.y = 0f;
        camRightLocal.y = 0f;

        // Safety: if camera is looking almost straight up/down, fallback to grid axes
        if (camFwdLocal.sqrMagnitude < 0.0001f) camFwdLocal = Vector3.forward;
        if (camRightLocal.sqrMagnitude < 0.0001f) camRightLocal = Vector3.right;

        camFwdLocal.Normalize();
        camRightLocal.Normalize();

        // Convert stick input into a direction in grid-local XZ
        Vector3 desiredLocal =
            camRightLocal * move.x +
            camFwdLocal * move.y;

        // Pick the dominant grid axis step (so we still do clean 1-cell moves)
        Vector3Int delta;
        if (Mathf.Abs(desiredLocal.x) > Mathf.Abs(desiredLocal.z))
        {
            delta = new Vector3Int(desiredLocal.x >= 0f ? 1 : -1, 0, 0);
        }
        else
        {
            delta = new Vector3Int(0, 0, desiredLocal.z >= 0f ? 1 : -1);
        }

        Vector3Int next = _anchorCell + delta;

        next.x = Mathf.Clamp(next.x, 0, grid.size.x - 1);
        next.y = Mathf.Clamp(next.y, 0, grid.size.y - 1);
        next.z = Mathf.Clamp(next.z, 0, grid.size.z - 1);

        _anchorCell = next;
        activeLayerY = _anchorCell.y;
    }

    void OnClick(InputAction.CallbackContext _)
    {
        if (_isPlacing)
        {
            TryPlaceHeld();
            return;
        }

        // If gamepad + we have a selected target, pick it without raycasting
        if (InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad)
        {
            if (TryPickupSelected())
                return;
        }

        TryPickupFromScene(); // mouse fallback
    }

    bool TryPickupSelected()
    {
        if (_selected == null) return false;
        if (!_selected.def) return false;

        // mimic your TryPickupFromScene body, but using pickup = _selected
        var pickup = _selected;
        SnapAnchorToPickup(pickup);

        _holdingExistingPlaced = (pickup.placedId != 0);

        _restoreOnCancel = false;
        _restoreCells.Clear();
        _restoreId = 0;

        if (_holdingExistingPlaced)
        {
            _restoreOnCancel = true;
            _restoreId = pickup.placedId;

            _restorePos = pickup.transform.position;
            _restoreRot = pickup.transform.rotation;
            _restoreScale = pickup.transform.localScale;
            _restoreWasActive = pickup.gameObject.activeSelf;

            if (!grid.TryGetPlacedCells(_restoreId, _restoreCells))
            {
                _restoreOnCancel = false;
                _restoreCells.Clear();
            }

            grid.Remove(_restoreId);
        }

        _heldPickup = pickup;

        _rot = Quaternion.Inverse(grid.origin.rotation) * _heldPickup.transform.rotation;

        if (TryGetRendererBounds(_heldPickup.gameObject, out var heldWorldBounds))
            _heldVisualCenterLocal = _heldPickup.transform.InverseTransformPoint(heldWorldBounds.center);
        else
            _heldVisualCenterLocal = Vector3.zero;

        currentDef = pickup.def;
        RebuildGhost();

        grid.placementMode = true;
        _isPlacing = true;

        _anchorCell.y = activeLayerY;

        if (pickup.hideOnPickup)
            pickup.gameObject.SetActive(false);

        // selection highlight should be cleared once we pick up
        ClearSelection();
        _selected = null;
        _targetIndex = -1;

        return true;
    }

    void OnConfirmPlacement(InputAction.CallbackContext _)
    {
        // Only confirm if we're currently placing something
        if (!_isPlacing) return;

        // Optional: prevent confirms if no held pickup/def (safety)
        if (currentDef == null || _heldPickup == null) return;

        TryPlaceHeld();
    }

    void TryPickupFromScene()
    {
        Vector2 screen = _player.Point.ReadValue<Vector2>();
        Ray r = cam.ScreenPointToRay(screen);

        if (!Physics.Raycast(r, out RaycastHit hit, pickupMaxDistance, pickupMask))
            return;

        var pickup = hit.collider.GetComponentInParent<PickupPiece>();
        if (!pickup || !pickup.def) return;

        _holdingExistingPlaced = (pickup.placedId != 0);

        _restoreOnCancel = false;
        _restoreCells.Clear();
        _restoreId = 0;

        if (_holdingExistingPlaced)
        {
            // Cache for cancel-revert
            _restoreOnCancel = true;
            _restoreId = pickup.placedId;

            _restorePos = pickup.transform.position;
            _restoreRot = pickup.transform.rotation;
            _restoreScale = pickup.transform.localScale;
            _restoreWasActive = pickup.gameObject.activeSelf;

            // IMPORTANT: grab the exact cells before removing
            if (!grid.TryGetPlacedCells(_restoreId, _restoreCells))
            {
                // Failsafe: if we can't restore cells, don't attempt revert
                _restoreOnCancel = false;
                _restoreCells.Clear();
            }

            grid.Remove(_restoreId);
        }

        _heldPickup = pickup;

        SnapAnchorToPickup(pickup);

        _rot = Quaternion.Inverse(grid.origin.rotation) * _heldPickup.transform.rotation;

        if (TryGetRendererBounds(_heldPickup.gameObject, out var heldWorldBounds))
            _heldVisualCenterLocal = _heldPickup.transform.InverseTransformPoint(heldWorldBounds.center);
        else
            _heldVisualCenterLocal = Vector3.zero;

        currentDef = pickup.def;
        RebuildGhost();

        grid.placementMode = true;
        _isPlacing = true;

        _anchorCell.y = activeLayerY;

        if (pickup.hideOnPickup)
            pickup.gameObject.SetActive(false);

        // If we had a cycled selection highlighted, clear it before picking up via raycast
        ClearSelection();
        _selected = null;
        _targetIndex = -1;
    }

    bool TryPlaceHeld()
    {
        if (!currentDef || _heldPickup == null) return false;

        if (!ComputeWorldCells(currentDef, _anchorCell, _rot, _tmpWorldCells)) return false;
        if (!grid.CanPlaceCells(_tmpWorldCells)) return false;
        if (!HasAnySupportBelow(_tmpWorldCells, out _, out _)) return false;
        if (!PassesFragileTopRule(currentDef, _tmpWorldCells)) return false;
        if (currentDef.mustBeStanding && !IsStandingFootprint(_tmpWorldCells)) return false;
        if (!PassesUprightRule(currentDef, _rot)) return false;

        int id = (_heldPickup.placedId != 0) ? _heldPickup.placedId : _nextPlacedId++;

        GameObject placed = _heldPickup.gameObject;

        _heldPickup.def = currentDef;
        _heldPickup.placedId = id;

        placed.transform.rotation = grid.origin.rotation * _rot;

        Vector3 targetCenterWorld = ComputeWorldBoundsCenter(_tmpWorldCells);
        placed.transform.position = targetCenterWorld - (placed.transform.rotation * _heldVisualCenterLocal);

        grid.Place(id, _tmpWorldCells);

        // pick a layer index that is included in pickupMask (eg "Pickup")
        int pickUpLayer = LayerMask.NameToLayer("PickUp");
        if (pickUpLayer == -1)
        {
            Debug.LogError("Layer 'PickUp' does not exist (case-sensitive). Add it in Project Settings > Tags and Layers.");
        }
        else
        {
            SetLayerRecursive(placed, pickUpLayer);
        }

        Physics.SyncTransforms();

        if (!placed.activeSelf)
            placed.SetActive(true);

        PlayPlaceSfx();
        StartCoroutine(PopScale(placed));

        ExitPlacementMode();
        return true;
    }

    void ExitPlacementMode(bool forceShowHeld = true)
    {
        _isPlacing = false;
        grid.placementMode = false;

        if (_ghost) Destroy(_ghost);
        _ghost = null;
        _ghostRenderers = null;

        if (_heldPickup)
        {
            if (forceShowHeld)
                _heldPickup.gameObject.SetActive(true);

            _heldPickup = null;
        }

        currentDef = null;
        _rot = Quaternion.identity;
        _holdingExistingPlaced = false;

        // clear cancel cache
        _restoreOnCancel = false;
        _restoreId = 0;
        _restoreCells.Clear();
    }

    void OnCancelPlacement(InputAction.CallbackContext _)
    {
        if (!_isPlacing) return;

        // If we picked up an existing placed piece, revert it.
        if (_restoreOnCancel && _heldPickup != null)
        {
            GameObject go = _heldPickup.gameObject;

            // Restore transform/active state
            go.transform.position = _restorePos;
            go.transform.rotation = _restoreRot;
            go.transform.localScale = _restoreScale;
            go.SetActive(_restoreWasActive);

            // Restore grid occupancy
            if (_restoreCells.Count > 0)
                grid.Place(_restoreId, _restoreCells);

            _heldPickup.placedId = _restoreId;
        }
        else
        {
            // Not previously placed; just show it again if it was hidden
            if (_heldPickup != null && _heldPickup.hideOnPickup)
                _heldPickup.gameObject.SetActive(true);
        }

        // IMPORTANT: don't force-enable here (we already restored active state)
        ExitPlacementMode(forceShowHeld: false);
    }

    void OnLayerScroll(InputAction.CallbackContext ctx)
    {
        if (!_isPlacing) return;
        if (Time.time < _nextScrollTime) return;

        Vector2 scroll = ctx.ReadValue<Vector2>();

        // Mouse wheel is usually in Y (up/down)
        float dy = scroll.y;
        if (Mathf.Abs(dy) < 0.01f) return;

        // Some mice report +/-120 per notch, some smaller.
        // Convert to “steps” robustly.
        int steps = Mathf.RoundToInt(dy / 120f);
        if (steps == 0) steps = (dy > 0f) ? 1 : -1;

        // Optional: clamp how many layers one event can jump
        steps = Mathf.Clamp(steps, -3, 3);

        SetLayer(activeLayerY + steps);

        _nextScrollTime = Time.time + scrollLayerCooldown;
    }

    void UpdateAnchorFromMouse()
    {
        Vector2 screen = _player.Point.ReadValue<Vector2>();
        Ray ray = cam.ScreenPointToRay(screen);

        // Fixed plane (layer 0 / floor) so XZ doesn't change when you change layers
        float baseYWorld = grid.CellToWorldCenter(new Vector3Int(0, 0, 0)).y;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, baseYWorld, 0f));

        if (!plane.Raycast(ray, out float enter))
            return;

        Vector3 hit = ray.GetPoint(enter);

        Vector3Int cell = grid.WorldToCell(hit);

        // Clamp XZ
        cell.x = Mathf.Clamp(cell.x, 0, grid.size.x - 1);
        cell.z = Mathf.Clamp(cell.z, 0, grid.size.z - 1);

        // Elevator: only Y changes with layer
        cell.y = Mathf.Clamp(activeLayerY, 0, grid.size.y - 1);

        _anchorCell = cell;
    }

    // --- Ghost ---

    void RebuildGhost()
    {
        if (_ghost) Destroy(_ghost);
        if (!currentDef || !currentDef.visualPrefab) return;

        _ghost = Instantiate(currentDef.visualPrefab);
        _ghost.name = $"Ghost_{currentDef.pieceName}";
        _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>(true);

        if (TryGetRendererBounds(_ghost, out var worldBounds))
            _ghostVisualCenterLocal = _ghost.transform.InverseTransformPoint(worldBounds.center);
        else
            _ghostVisualCenterLocal = Vector3.zero;
    }

    void SetGhostMaterial(Material mat)
    {
        if (!mat || _ghostRenderers == null) return;
        for (int i = 0; i < _ghostRenderers.Length; i++)
            _ghostRenderers[i].sharedMaterial = mat;
    }

    void UpdateGhostTransform()
    {
        if (!_ghost) return;

        _ghost.transform.rotation = grid.origin.rotation * _rot;

        Vector3 targetCenterWorld =
            (_tmpWorldCells.Count > 0) ? ComputeWorldBoundsCenter(_tmpWorldCells)
                                       : grid.CellToWorldCenter(_anchorCell);

        _ghost.transform.position = targetCenterWorld - (_ghost.transform.rotation * _ghostVisualCenterLocal);
    }

    // --- Placement math ---

    static bool ComputeWorldCells(PieceDefinition def, Vector3Int anchor, Quaternion rotLocal, List<Vector3Int> outCells)
    {
        outCells.Clear();
        if (def == null || def.occupiedCellsLocal == null || def.occupiedCellsLocal.Length == 0)
            return false;

        Vector3Int pivot = def.pivotLocal;

        for (int i = 0; i < def.occupiedCellsLocal.Length; i++)
        {
            Vector3Int local = def.occupiedCellsLocal[i];

            Vector3 rel = (Vector3)(local - pivot);
            Vector3 rotatedRel = rotLocal * rel;

            Vector3Int r = new Vector3Int(
                Mathf.RoundToInt(rotatedRel.x),
                Mathf.RoundToInt(rotatedRel.y),
                Mathf.RoundToInt(rotatedRel.z)
            );

            outCells.Add(anchor + r);
        }

        return true;
    }

    Vector3 ComputeWorldBoundsCenter(IReadOnlyList<Vector3Int> worldCells)
    {
        Vector3 min = grid.CellToWorldCenter(worldCells[0]);
        Vector3 max = min;

        for (int i = 1; i < worldCells.Count; i++)
        {
            Vector3 w = grid.CellToWorldCenter(worldCells[i]);
            min = Vector3.Min(min, w);
            max = Vector3.Max(max, w);
        }

        return (min + max) * 0.5f;
    }

    static bool TryGetRendererBounds(GameObject go, out Bounds b)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        b = default;
        bool has = false;

        for (int i = 0; i < rs.Length; i++)
        {
            if (!has) { b = rs[i].bounds; has = true; }
            else b.Encapsulate(rs[i].bounds);
        }

        return has;
    }

    void Cycle(int dir)
    {
        if (_targets.Count == 0)
        {
            RefreshTargets();
            if (_targets.Count == 0) return;
        }

        _targetIndex = (_targetIndex < 0) ? 0 : _targetIndex;

        _targetIndex += dir;
        if (_targetIndex < 0) _targetIndex = _targets.Count - 1;
        if (_targetIndex >= _targets.Count) _targetIndex = 0;

        SelectIndex(_targetIndex);
    }

    void SelectIndex(int idx)
    {
        ClearSelection();

        if (idx < 0 || idx >= _targets.Count)
        {
            _selected = null;
            return;
        }

        _selected = _targets[idx];
        if (!_selected) return;

        // Turn highlight ON (no material swapping)
        _selected.GetComponent<PieceHighlighter>()?.SetHighlight(true);
    }

    void ClearSelection()
    {
        if (_selected != null)
            _selected.GetComponent<PieceHighlighter>()?.SetHighlight(false);
    }

    void PlayPlaceSfx()
    {
        if (!sfxSource) return;

        AudioClip clip = (currentDef && currentDef.placeClipOverride) ? currentDef.placeClipOverride : placeClip;
        if (!clip) return;

        float vol = (currentDef != null) ? (placeVolume * currentDef.placeVolume) : placeVolume;

        Vector2 pr = (currentDef != null) ? currentDef.placePitchRange : placePitchRange;
        float p = Random.Range(pr.x, pr.y);

        sfxSource.pitch = p;
        sfxSource.PlayOneShot(clip, vol);
    }

    void SnapAnchorToPickup(PickupPiece pickup)
    {
        Vector3 worldPoint = pickup.transform.position;
        if (TryGetRendererBounds(pickup.gameObject, out var b))
            worldPoint = b.center;

        Vector3Int cell = grid.WorldToCell(worldPoint);

        // Clamp XZ only
        cell.x = Mathf.Clamp(cell.x, 0, grid.size.x - 1);
        cell.z = Mathf.Clamp(cell.z, 0, grid.size.z - 1);

        // KEEP current layer
        cell.y = Mathf.Clamp(activeLayerY, 0, grid.size.y - 1);

        _anchorCell = cell;
    }

    IEnumerator PopScale(GameObject go)
    {
        if (!go) yield break;

        Transform t = go.transform;
        Vector3 baseScale = t.localScale;

        float half = Mathf.Max(0.0001f, popDuration * 0.5f);

        float t01 = 0f;
        while (t01 < 1f)
        {
            t01 += Time.deltaTime / half;
            float s = Mathf.Lerp(1f, popUpScale, t01);
            t.localScale = baseScale * s;
            yield return null;
        }

        t01 = 0f;
        while (t01 < 1f)
        {
            t01 += Time.deltaTime / half;
            float s = Mathf.Lerp(popUpScale, popDownScale, t01);
            t.localScale = baseScale * s;
            yield return null;
        }

        t.localScale = baseScale;
    }

    // --- rules / debug unchanged ---

    void RefreshTargets()
    {
        _targets.Clear();

        var all = Object.FindObjectsByType<PickupPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        Vector3 camPos = cam.transform.position;
        Vector3 camFwd = cam.transform.forward;

        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (!p || !p.def) continue;

            float dist = Vector3.Distance(camPos, p.transform.position);
            if (dist > maxSelectDistance) continue;

            _targets.Add(p);
        }

        // Sort by "best to select"
        _targets.Sort((a, b) => ScoreTarget(b).CompareTo(ScoreTarget(a)));

        // Keep current selection if possible
        if (_selected != null)
        {
            int idx = _targets.IndexOf(_selected);
            if (idx >= 0) { _targetIndex = idx; return; }
        }

        _targetIndex = (_targets.Count > 0) ? 0 : -1;
        SelectIndex(_targetIndex);
    }

    // Higher score = better
    float ScoreTarget(PickupPiece p)
    {
        Vector3 camPos = cam.transform.position;
        Vector3 camFwd = cam.transform.forward;

        Vector3 to = (p.transform.position - camPos);
        float dist = to.magnitude;
        if (dist < 0.001f) dist = 0.001f;
        Vector3 dir = to / dist;

        // Prefer in front of camera
        float forwardDot = Vector3.Dot(camFwd, dir); // -1..1

        // Prefer closer
        float closeScore = 1f / dist;

        // Prefer visible (optional raycast check)
        float visScore = HasLineOfSight(p) ? 1f : 0f;

        // Weighting: tweak to taste
        return (forwardDot * 2.0f) + (closeScore * 4.0f) + (visScore * 1.0f);
    }

    bool HasLineOfSight(PickupPiece p)
    {
        // If you have a dedicated occlusion layer mask, use it.
        // For now, raycast against everything and see if we hit that piece first.
        Vector3 origin = cam.transform.position;
        Vector3 target = p.transform.position;
        Vector3 dir = (target - origin);
        float dist = dir.magnitude;
        if (dist < 0.001f) return true;
        dir /= dist;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist))
        {
            return hit.collider && hit.collider.GetComponentInParent<PickupPiece>() == p;
        }
        return true;
    }

    bool HasAnySupportBelow(IReadOnlyList<Vector3Int> cells, out Vector3Int supportedCell, out Vector3Int supportBelow)
    {
        supportedCell = default;
        supportBelow = default;

        if (cells == null || cells.Count == 0) return false;

        int minY = int.MaxValue;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].y < minY) minY = cells[i].y;

        if (minY == 0) return true;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int below = cells[i] + Vector3Int.down;

            bool belowIsSelf = false;
            for (int j = 0; j < cells.Count; j++)
            {
                if (cells[j] == below) { belowIsSelf = true; break; }
            }
            if (belowIsSelf) continue;

            if (grid.IsOccupied(below))
            {
                supportedCell = cells[i];
                supportBelow = below;
                return true;
            }
        }

        return false;
    }

    bool PassesFragileTopRule(PieceDefinition def, IReadOnlyList<Vector3Int> cells)
    {
        if (def == null || !def.fragileTop) return true;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int above = cells[i] + Vector3Int.up;
            if (grid.IsInside(above) && grid.IsOccupied(above))
                return false;
        }
        return true;
    }

    bool IsStandingFootprint(IReadOnlyList<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return false;

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue;

        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
            if (c.z < minZ) minZ = c.z; if (c.z > maxZ) maxZ = c.z;
        }

        int sizeX = (maxX - minX) + 1;
        int sizeY = (maxY - minY) + 1;
        int sizeZ = (maxZ - minZ) + 1;

        return sizeX == 1 && sizeY == 2 && sizeZ == 1;
    }

    bool PassesUprightRule(PieceDefinition def, Quaternion rotLocal)
    {
        if (!def || !def.forbidUpsideDown) return true;
        Vector3 up = rotLocal * Vector3.up;
        return Vector3.Dot(up, Vector3.up) > 0.0f;
    }

    static Quaternion Normalize(Quaternion q)
    {
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (mag > 0.00001f)
        {
            float inv = 1f / mag;
            q.x *= inv; q.y *= inv; q.z *= inv; q.w *= inv;
        }
        return q;
    }

    void DebugDrawCells(List<Vector3Int> cells)
    {
        if (cells == null) return;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 w = grid.CellToWorldCenter(cells[i]);
            Debug.DrawLine(w + Vector3.up * 0.25f, w - Vector3.up * 0.25f, Color.yellow, 0f);
            Debug.DrawLine(w + Vector3.right * 0.25f, w - Vector3.right * 0.25f, Color.yellow, 0f);
            Debug.DrawLine(w + Vector3.forward * 0.25f, w - Vector3.forward * 0.25f, Color.yellow, 0f);
        }
    }

    void DebugDrawSupport(IReadOnlyList<Vector3Int> cells, bool hasSupport, Vector3Int supportedCell, Vector3Int supportBelow)
    {
        // keep your existing implementation here if you want the nice debug lines
        // (omitted for brevity since unchanged)
    }

    void OnGUI()
    {
        if (!_isPlacing) return;
        if (string.IsNullOrEmpty(_placementWarning)) return;

        GUI.color = Color.yellow;
        GUI.Label(new Rect(12, 12, 600, 24), _placementWarning);
    }
}