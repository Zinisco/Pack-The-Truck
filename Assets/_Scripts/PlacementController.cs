using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementController : MonoBehaviour
{
    public enum PlacementMoveMode { Mouse, GridStep }

    [Header("Puzzle")]
    [SerializeField] private PackPuzzleValidator puzzleValidator;

    [Header("Refs")]
    public Camera cam;
    public GridManager grid;

    [Header("Piece Selection")]
    public PieceDefinition currentDef;

    [Header("Movement")]
    public float keyRepeatDelay = 0.18f;

    [Header("Placement Move Mode")]
    public PlacementMoveMode moveMode = PlacementMoveMode.Mouse;

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

    [Header("Auto Drop (Tetris-style)")]
    public bool autoDropY = true;
    public bool dropFromTop = true;      // true = drop starting from top of grid, false = from current y
    public int dropSearchPadding = 1;    // optional, lets you search slightly above current y for stability

    [Header("Pop Animation")]
    public float popDuration = 0.12f;
    public float popUpScale = 1.08f;
    public float popDownScale = 0.98f;

    [Header("Rotation Smoothing")]
    public bool smoothRotation = true;
    public float rotateSpeed = 18f; // higher = snappier (degrees-ish feel)

    Quaternion _rotTarget = Quaternion.identity;
    Quaternion _rotVisual = Quaternion.identity;

    [Header("Support Debug")]
    public bool debugDrawSupport = true;
    public bool debugDrawUnsupported = true;
    public float debugLineHeight = 0.35f;

    [Header("Gamepad Selection (Cycle)")]
    public bool enableCycleSelect = true;
    public float selectionRefreshInterval = 0.35f;   // how often to rebuild list automatically
    public float maxSelectDistance = 100f;           // ignore super far pieces (optional)
    public System.Action<PieceDefinition, int> OnPlaced;

    [Header("UI Spawn")]
    public bool spawnOnSelect = true;
    public Transform spawnedPiecesParent; // optional: keeps hierarchy tidy (e.g. "PlacedPieces")
    private int activeLayerY = 0;

    bool _spawnedFromUI = false;

    List<PickupPiece> _targets = new();
    int _targetIndex = -1;
    float _nextRefreshTime;

    PickupPiece _selected;

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    ControlSchemeMode _lastScheme = ControlSchemeMode.KeyboardMouse;

    bool _isPlacing = false;
    public bool IsPlacing => _isPlacing;

    PickupPiece _heldPickup;

    GameObject _ghost;
    Renderer[] _ghostRenderers;

    Vector3Int _anchorCell;

    Vector3 _ghostVisualCenterLocal;
    Vector3 _heldVisualCenterLocal;

    bool _holdingExistingPlaced;
    string _placementWarning;

    float _nextMoveTime = 0f;

    int _nextPlacedId = 1;
    readonly List<Vector3Int> _tmpWorldCells = new();
    readonly Dictionary<int, bool> _placedFragile = new Dictionary<int, bool>();
    readonly Dictionary<int, PieceWeight> _placedWeight = new Dictionary<int, PieceWeight>();
    readonly Dictionary<int, PieceDefinition> _placedDefs = new Dictionary<int, PieceDefinition>();

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
        _player.ConfirmPlacement.performed += OnConfirmPlacement;
        _player.CyclePrev.performed += OnCyclePrev;
        _player.CycleNext.performed += OnCycleNext;

        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        _player.Click.performed -= OnClick;
        _player.ConfirmPlacement.performed -= OnConfirmPlacement;
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

        var scheme = (InputHub.Instance != null) ? InputHub.Instance.ActiveScheme : ControlSchemeMode.KeyboardMouse;

        // If we just switched away from gamepad, kill any highlight immediately.
        if (_lastScheme != scheme)
        {
            if (scheme == ControlSchemeMode.KeyboardMouse)
                ClearSelection();

            _lastScheme = scheme;
        }

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
            Vector2 move = _player.PieceMove.ReadValue<Vector2>();
            if (move.sqrMagnitude < 0.15f * 0.15f) move = Vector2.zero;

            // Step in XZ only
            StepMoveXZ(move);

            // After XZ change, drop to rest
            if (autoDropY) SnapToDropY();
        }
        else
        {
            // Mouse controls XZ, then auto-drop decides Y
            if (!isOrbiting)
            {
                UpdateAnchorXZFromMouse();
                if (autoDropY) SnapToDropY();
            }
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

        if (smoothRotation)
        {
            // exponential smoothing that feels consistent regardless of framerate
            float t = 1f - Mathf.Exp(-rotateSpeed * Time.deltaTime);
            _rotVisual = Quaternion.Slerp(_rotVisual, _rotTarget, t);
        }
        else
        {
            _rotVisual = _rotTarget;
        }

        // --- VALIDATION ---
        bool computed = ComputeWorldCells(currentDef, _anchorCell, _rotTarget, _tmpWorldCells);
        bool hasSpace = computed && grid.CanPlaceCells(_tmpWorldCells);

        bool hasSupport = false;
        Vector3Int supportedCell = default, supportBelow = default;
        if (computed) hasSupport = HasAnySupportBelow(_tmpWorldCells, out supportedCell, out supportBelow);

        bool fragileOk = computed && PassesFragileTopRule(currentDef, _tmpWorldCells);
        bool standingOk = !currentDef.mustBeStanding || (computed && IsStandingFootprint(currentDef, _tmpWorldCells));
        bool uprightOk = PassesUprightRule(currentDef, _rotTarget);

        bool weightOk = true;
        string weightReason = null;
        if (computed)
            weightOk = PassesWeightSupportRule(currentDef, _tmpWorldCells, out weightReason);

        bool canPlace = computed && hasSpace && hasSupport && fragileOk && weightOk && standingOk && uprightOk;

        _placementWarning = null;
        if (!computed) _placementWarning = "Invalid shape / rotation.";
        else if (!hasSpace) _placementWarning = "Blocked: space is occupied.";
        else if (!hasSupport) _placementWarning = "Needs support below.";
        else if (!fragileOk) _placementWarning = "Fragile: nothing can be directly on top.";
        else if (!weightOk) _placementWarning = weightReason ?? "Not enough support for weight.";
        else if (!standingOk) _placementWarning = "Must be standing upright.";
        else if (!uprightOk) _placementWarning = "Can't place upside down.";

        if (computed && debugDrawSupport)
            DebugDrawSupport(_tmpWorldCells, hasSupport, supportedCell, supportBelow);

        DebugDrawCells(_tmpWorldCells);
        UpdateGhostTransform();
        SetGhostMaterial(canPlace ? validMat : invalidMat);
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
        _rotTarget = Quaternion.AngleAxis(delta, Vector3.up) * _rotTarget;
        _rotTarget = Normalize(_rotTarget);
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
        _rotTarget = Quaternion.AngleAxis(degrees, axis) * _rotTarget;
        _rotTarget = Normalize(_rotTarget);
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

        _anchorCell.x = next.x;
        _anchorCell.z = next.z;
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
            else
            {
                if (IsSupportingOtherPiece(_restoreId, _restoreCells, out int aboveId, out _, out _))
                {
                    _placementWarning = $"Can't move: piece is supporting another piece (ID {aboveId}).";
                    _restoreOnCancel = false;
                    _restoreCells.Clear();
                    return false;
                }
            }

            grid.Remove(_restoreId);
            _placedDefs.Remove(_restoreId);
            _placedFragile.Remove(_restoreId);
            _placedWeight.Remove(_restoreId);
        }

        _heldPickup = pickup;

        _rotTarget = Quaternion.Inverse(grid.origin.rotation) * _heldPickup.transform.rotation;
        _rotTarget = Normalize(_rotTarget);
        _rotVisual = _rotTarget;

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
            else
            {
                // Block moving if this piece is supporting something above it
                if (IsSupportingOtherPiece(_restoreId, _restoreCells, out int aboveId, out _, out _))
                {
                    _placementWarning = $"Can't move: piece is supporting another piece (ID {aboveId}).";
                    _restoreOnCancel = false;
                    _restoreCells.Clear();
                    return; // do not pick up
                }
            }

            grid.Remove(_restoreId);
            _placedDefs.Remove(_restoreId);
            _placedFragile.Remove(_restoreId);
            _placedWeight.Remove(_restoreId);
        }

        _heldPickup = pickup;

        SnapAnchorToPickup(pickup);

        _rotTarget = Quaternion.Inverse(grid.origin.rotation) * _heldPickup.transform.rotation;
        _rotTarget = Normalize(_rotTarget);
        _rotVisual = _rotTarget;

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

        if (!ComputeWorldCells(currentDef, _anchorCell, _rotTarget, _tmpWorldCells)) return false;
        if (!grid.CanPlaceCells(_tmpWorldCells)) return false;
        if (!HasAnySupportBelow(_tmpWorldCells, out _, out _)) return false;
        if (!PassesWeightSupportRule(currentDef, _tmpWorldCells, out _)) return false;
        if (!PassesFragileTopRule(currentDef, _tmpWorldCells)) return false;
        if (currentDef.mustBeStanding && !IsStandingFootprint(currentDef, _tmpWorldCells)) return false;
        if (!PassesUprightRule(currentDef, _rotTarget)) return false;

        bool isNewFromUI = _spawnedFromUI;
        PieceDefinition placedDef = currentDef;

        int id = (_heldPickup.placedId != 0) ? _heldPickup.placedId : _nextPlacedId++;
        GameObject placed = _heldPickup.gameObject;

        _heldPickup.def = placedDef;
        _heldPickup.placedId = id;

        placed.transform.rotation = grid.origin.rotation * _rotTarget;

        Vector3 targetCenterWorld = ComputeWorldBoundsCenter(_tmpWorldCells);
        placed.transform.position = targetCenterWorld - (placed.transform.rotation * _heldVisualCenterLocal);

        grid.Place(id, _tmpWorldCells);

        _placedFragile[id] = placedDef.fragileTop;
        _placedWeight[id] = placedDef.weight;
        _placedDefs[id] = placedDef;

        int pickUpLayer = LayerMask.NameToLayer("PickUp");
        if (pickUpLayer != -1) SetLayerRecursive(placed, pickUpLayer);

        Physics.SyncTransforms();
        if (!placed.activeSelf) placed.SetActive(true);

        PlayPlaceSfx();
        StartCoroutine(PopScale(placed));

        // Exit placement FIRST so auto-advance won't cancel/destroy the placed piece
        ExitPlacementMode();

        // UI can auto-advance and start the next placement
        if (isNewFromUI)
            OnPlaced?.Invoke(placedDef, id);

        if (puzzleValidator != null && puzzleValidator.IsSolved())
        {
            Debug.Log("YOU WIN");
        }

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

        _spawnedFromUI = false;
        currentDef = null;
        _rotTarget = Quaternion.identity;
        _holdingExistingPlaced = false;

        // clear cancel cache
        _restoreOnCancel = false;
        _restoreId = 0;
        _restoreCells.Clear();
    }

    public void CancelPlacement()
    {
        if (!_isPlacing) return;
        CancelCurrentPlacementInternal();
    }

    void UpdateAnchorXZFromMouse()
    {
        Vector2 screen = _player.Point.ReadValue<Vector2>();
        Ray ray = cam.ScreenPointToRay(screen);

        // Use a plane at the grid’s “floor” height
        float baseYWorld = grid.CellToWorldCenter(new Vector3Int(0, 0, 0)).y;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, baseYWorld, 0f));

        if (!plane.Raycast(ray, out float enter))
            return;

        Vector3 hit = ray.GetPoint(enter);
        Vector3Int cell = grid.WorldToCell(hit);

        cell.x = Mathf.Clamp(cell.x, 0, grid.size.x - 1);
        cell.z = Mathf.Clamp(cell.z, 0, grid.size.z - 1);

        // DO NOT set Y here (auto drop will)
        _anchorCell.x = cell.x;
        _anchorCell.z = cell.z;
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

        _ghost.transform.rotation = grid.origin.rotation * _rotVisual;

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

    void SnapToDropY()
    {
        if (!currentDef) return;

        int startY;
        if (dropFromTop)
            startY = grid.size.y - 1;
        else
            startY = Mathf.Clamp(_anchorCell.y + dropSearchPadding, 0, grid.size.y - 1);

        // We’ll search downward for the first valid “resting” placement
        for (int y = startY; y >= 0; y--)
        {
            var testAnchor = new Vector3Int(_anchorCell.x, y, _anchorCell.z);

            // must produce cells
            if (!ComputeWorldCells(currentDef, testAnchor, _rotTarget, _tmpWorldCells))
                continue;

            // must be inside bounds
            if (!AllInside(_tmpWorldCells))
                continue;

            // must not overlap
            if (!grid.CanPlaceCells(_tmpWorldCells))
                continue;

            // must obey “must be standing” / upright rule here too (so drop respects it)
            if (currentDef.mustBeStanding && !IsStandingFootprint(currentDef, _tmpWorldCells))
                continue;

            if (!PassesUprightRule(currentDef, _rotTarget))
                continue;

            // must be supported (or touch floor)
            if (!HasAnySupportBelow(_tmpWorldCells, out _, out _))
                continue;

            // Found the resting Y
            _anchorCell.y = y;
            return;
        }

        // If nothing works, keep current Y (or clamp)
        _anchorCell.y = Mathf.Clamp(_anchorCell.y, 0, grid.size.y - 1);
    }

    bool AllInside(IReadOnlyList<Vector3Int> cells)
    {
        for (int i = 0; i < cells.Count; i++)
            if (!grid.IsInside(cells[i]))
                return false;
        return true;
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

        // Only show highlight on Gamepad
        bool usingGamepad = (InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad);
        if (usingGamepad)
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

    public void SetCurrentPiece(PieceDefinition def)
    {
        currentDef = def;

        // Reset rotation
        _rotTarget = Quaternion.identity;

        if (_isPlacing)
            RebuildGhost();
    }

    public bool BeginPlaceFromUI(PieceDefinition def)
    {
        if (!def || !def.visualPrefab) return false;
        if (!cam || !grid) return false;

        // If we're already placing something, cancel it first
        if (_isPlacing)
        {
            // Simulate a cancel: destroy spawned temp piece if needed, exit placement
            CancelCurrentPlacementInternal();
        }

        // Spawn a new pickup object (hidden while placing)
        _heldPickup = SpawnPickupForDef(def);
        if (_heldPickup == null) return false;

        _spawnedFromUI = true;
        _holdingExistingPlaced = false;

        currentDef = def;

        // Reset rotation
        _rotTarget = Quaternion.identity;
        _rotVisual = _rotTarget;

        // Start placement mode
        grid.placementMode = true;
        _isPlacing = true;

        // Pick a reasonable starting anchor (center-ish of current layer)
        Vector3 centerWorld = grid.GetWorldLayerCenter(activeLayerY, useCellCenter: true);
        Vector3Int cell = grid.WorldToCell(centerWorld);
        cell.x = Mathf.Clamp(cell.x, 0, grid.size.x - 1);
        cell.z = Mathf.Clamp(cell.z, 0, grid.size.z - 1);
        cell.y = Mathf.Clamp(activeLayerY, 0, grid.size.y - 1);
        _anchorCell = cell;

        // Compute held visual center (used for final placement position)
        if (TryGetRendererBounds(_heldPickup.gameObject, out var heldWorldBounds))
            _heldVisualCenterLocal = _heldPickup.transform.InverseTransformPoint(heldWorldBounds.center);
        else
            _heldVisualCenterLocal = Vector3.zero;

        // Build ghost
        RebuildGhost();

        // Ensure layer is correct for later pickup
        int pickUpLayer = LayerMask.NameToLayer("PickUp");
        if (pickUpLayer != -1)
            SetLayerRecursive(_heldPickup.gameObject, pickUpLayer);

        return true;
    }

    PickupPiece SpawnPickupForDef(PieceDefinition def)
    {
        GameObject root = new GameObject($"Piece_{def.pieceName}");
        if (spawnedPiecesParent) root.transform.SetParent(spawnedPiecesParent, true);

        var pickup = root.AddComponent<PickupPiece>();
        pickup.def = def;
        pickup.placedId = 0;
        pickup.hideOnPickup = true;

        GameObject visual = Instantiate(def.visualPrefab, root.transform);
        visual.name = "Visual";

        // Strip any accidental PickupPiece components in the visual hierarchy
        var extras = visual.GetComponentsInChildren<PickupPiece>(true);
        for (int i = 0; i < extras.Length; i++)
            Destroy(extras[i]);

        root.SetActive(false);
        return pickup;
    }

    void CancelCurrentPlacementInternal()
    {
        // If it was a UI-spawned piece, destroy it on cancel
        if (_heldPickup != null && _spawnedFromUI)
        {
            Destroy(_heldPickup.gameObject);
        }
        else
        {
            // Otherwise behave like your normal cancel path (restore / show)
            if (_restoreOnCancel && _heldPickup != null)
            {
                GameObject go = _heldPickup.gameObject;

                go.transform.position = _restorePos;
                go.transform.rotation = _restoreRot;
                go.transform.localScale = _restoreScale;
                go.SetActive(_restoreWasActive);

                if (_restoreCells.Count > 0)
                    grid.Place(_restoreId, _restoreCells);

                _placedFragile[_restoreId] = (_heldPickup.def != null && _heldPickup.def.fragileTop);
                _placedWeight[_restoreId] = (_heldPickup.def != null) ? _heldPickup.def.weight : PieceWeight.Normal;
                _placedDefs[_restoreId] = _heldPickup.def;

                _heldPickup.placedId = _restoreId;
            }
            else
            {
                if (_heldPickup != null && _heldPickup.hideOnPickup)
                    _heldPickup.gameObject.SetActive(true);
            }
        }

        // exit placement without forcing show (we already handled it)
        ExitPlacementMode(forceShowHeld: false);

        _spawnedFromUI = false;
    }

    void SnapAnchorToPickup(PickupPiece pickup)
    {
        Vector3 worldPoint = pickup.transform.position;

        if (TryGetRendererBounds(pickup.gameObject, out var b))
        {
            // Use bottom of the object so we select the correct layer (prevents Y jumping)
            worldPoint = new Vector3(b.center.x, b.min.y + 0.01f, b.center.z);
        }

        Vector3Int cell = grid.WorldToCell(worldPoint);

        cell.x = Mathf.Clamp(cell.x, 0, grid.size.x - 1);
        cell.y = Mathf.Clamp(cell.y, 0, grid.size.y - 1);
        cell.z = Mathf.Clamp(cell.z, 0, grid.size.z - 1);

        activeLayerY = cell.y;
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
            if (!t) yield break; 
            t01 += Time.deltaTime / half;
            float s = Mathf.Lerp(1f, popUpScale, t01);
            t.localScale = baseScale * s;
            yield return null;
        }

        t01 = 0f;
        while (t01 < 1f)
        {
            if (!t) yield break; 
            t01 += Time.deltaTime / half;
            float s = Mathf.Lerp(popUpScale, popDownScale, t01);
            t.localScale = baseScale * s;
            yield return null;
        }

        if (t) t.localScale = baseScale; 
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

    public int GetPlacedCount(PieceDefinition def)
    {
        if (def == null) return 0;

        int count = 0;
        foreach (var kvp in _placedDefs)
        {
            if (kvp.Value == def)
                count++;
        }
        return count;
    }

    public Dictionary<PieceDefinition, int> GetPlacedCounts()
    {
        Dictionary<PieceDefinition, int> counts = new Dictionary<PieceDefinition, int>();

        foreach (var kvp in _placedDefs)
        {
            PieceDefinition def = kvp.Value;
            if (def == null) continue;

            if (!counts.ContainsKey(def))
                counts[def] = 0;

            counts[def]++;
        }

        return counts;
    }

    bool HasAnySupportBelow(IReadOnlyList<Vector3Int> cells, out Vector3Int supportedCell, out Vector3Int supportBelow)
    {
        supportedCell = default;
        supportBelow = default;

        if (cells == null || cells.Count == 0) return false;

        int minY = int.MaxValue;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].y < minY) minY = cells[i].y;

        // touching floor is always supported
        if (minY == 0) return true;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int below = cells[i] + Vector3Int.down;

            // ignore self-support (stacked cells within the same piece)
            bool belowIsSelf = false;
            for (int j = 0; j < cells.Count; j++)
            {
                if (cells[j] == below) { belowIsSelf = true; break; }
            }
            if (belowIsSelf) continue;

            if (!grid.IsInside(below)) continue;
            if (!grid.IsOccupied(below)) continue;

            int occId = grid.GetOccupantId(below);

            // If the thing below is fragile-top, it cannot be used as support.
            if (occId != 0 && _placedFragile.TryGetValue(occId, out bool isFragile) && isFragile)
                continue;

            supportedCell = cells[i];
            supportBelow = below;
            return true;
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

    bool IsStandingFootprint(PieceDefinition def, IReadOnlyList<Vector3Int> cells)
    {
        if (def == null || cells == null || cells.Count == 0) return false;

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

        Vector3Int actual = new Vector3Int(sizeX, sizeY, sizeZ);
        Vector3Int want = def.standingBounds;

        // exact match
        if (actual == want) return true;

        // allow yaw swap of X/Z (common case)
        if (actual.x == want.z && actual.y == want.y && actual.z == want.x)
            return true;

        return false;
    }

    bool PassesUprightRule(PieceDefinition def, Quaternion rotLocal)
    {
        if (!def || !def.forbidUpsideDown) return true;
        Vector3 up = rotLocal * Vector3.up;
        return Vector3.Dot(up, Vector3.up) > 0.0f;
    }

    int RequiredSupportCapacity(PieceWeight w)
    {
        switch (w)
        {
            case PieceWeight.Light: return 1;
            case PieceWeight.Normal: return 2;
            case PieceWeight.Heavy: return 3;
            default: return 2;
        }
    }

    int SupportCapacityProvidedBy(PieceWeight w)
    {
        switch (w)
        {
            case PieceWeight.Light: return 1;
            case PieceWeight.Normal: return 2;
            case PieceWeight.Heavy: return 3;
            default: return 2;
        }
    }

    // Checks if the piece's bottom cells are supported with enough total capacity.
    // IMPORTANT: fragile pieces below do NOT count as support (keeps your fragile-overhang rule intact).
    // Capacity is counted by DISTINCT supporting pieces (unique placedId), not by number of cells.

    bool PassesWeightSupportRule(PieceDefinition def, IReadOnlyList<Vector3Int> cells, out string reason)
    {
        reason = null;
        if (def == null || cells == null || cells.Count == 0) return false;

        // On the floor -> always supported
        int minY = int.MaxValue;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].y < minY) minY = cells[i].y;

        if (minY == 0) return true;

        // Collect DISTINCT supporting piece IDs under bottom-layer cells
        HashSet<int> supportIds = new HashSet<int>();

        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.y != minY) continue;

            Vector3Int below = c + Vector3Int.down;
            if (!grid.IsInside(below)) continue;

            int occId = grid.GetOccupantId(below);
            if (occId == 0) continue;

            // If the thing below is fragile-top, it cannot be used as support (overhang still allowed)
            if (_placedFragile.TryGetValue(occId, out bool isFragile) && isFragile)
                continue;

            supportIds.Add(occId);
        }

        if (supportIds.Count == 0)
        {
            reason = "Needs non-fragile support below.";
            return false;
        }

        // Sum capacity from DISTINCT supports
        int capacity = 0;
        foreach (int id in supportIds)
        {
            PieceWeight w = PieceWeight.Normal;
            if (_placedWeight.TryGetValue(id, out var stored)) w = stored;

            capacity += SupportCapacityProvidedBy(w);
        }

        int needed = RequiredSupportCapacity(def.weight);

        if (capacity < needed)
        {
            reason = $"Too heavy: needs support capacity {needed} (has {capacity}).";
            return false;
        }

        return true;
    }

    bool IsSupportingOtherPiece(int myPlacedId, IReadOnlyList<Vector3Int> myCells,
    out int aboveId, out Vector3Int myCell, out Vector3Int aboveCell)
    {
        aboveId = 0;
        myCell = default;
        aboveCell = default;

        if (myPlacedId == 0 || myCells == null || myCells.Count == 0) return false;

        for (int i = 0; i < myCells.Count; i++)
        {
            Vector3Int c = myCells[i];
            Vector3Int up = c + Vector3Int.up;

            if (!grid.IsInside(up)) continue;
            if (!grid.IsOccupied(up)) continue;

            int occ = grid.GetOccupantId(up);

            // if something above is a different placed piece, we're supporting it
            if (occ != 0 && occ != myPlacedId)
            {
                aboveId = occ;
                myCell = c;
                aboveCell = up;
                return true;
            }
        }
        return false;
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