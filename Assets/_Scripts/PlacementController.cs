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

    [Header("Y Layer")]
    public float yLayerRepeatDelay = 0.15f;
    private float _nextYLayerMoveTime = 0f;

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

    [Header("Pop Animation")]
    public float popDuration = 0.12f;
    public float popUpScale = 1.08f;
    public float popDownScale = 0.98f;

    [Header("Rotation Smoothing")]
    public bool smoothRotation = true;
    public float rotateSpeed = 18f; // higher = snappier (degrees-ish feel)

    Quaternion _rotTarget = Quaternion.identity;
    Quaternion _rotVisual = Quaternion.identity;

    [Header("Gamepad Selection (Cycle)")]
    public bool enableCycleSelect = true;
    public float selectionRefreshInterval = 0.35f;   // how often to rebuild list automatically
    public float maxSelectDistance = 100f;           // ignore super far pieces (optional)
    public System.Action<PieceDefinition, int> OnPlaced;
    public System.Action<PieceDefinition, int> OnReturned;

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
        _player.DeletePiece.performed += OnDeletePiece;
        _player.ChangeYLayer.performed += OnChangeYLayer;

        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        _player.Click.performed -= OnClick;
        _player.ConfirmPlacement.performed -= OnConfirmPlacement;
        _player.CyclePrev.performed -= OnCyclePrev;
        _player.CycleNext.performed -= OnCycleNext;
        _player.DeletePiece.performed -= OnDeletePiece;
        _player.ChangeYLayer.performed -= OnChangeYLayer;

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

            StepMoveXZ(move);
        }
        else
        {
            if (!isOrbiting)
            {
                UpdateAnchorXZFromMouse();
            }
        }

        // always use manual Y layer
        _anchorCell.y = Mathf.Clamp(activeLayerY, 0, grid.size.y - 1);

        // Rotate
        HandleRotationInputActions();

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
        bool canPlace = computed && grid.CanPlaceCells(_tmpWorldCells);

        _placementWarning = null;
        if (!computed) _placementWarning = "Invalid shape / rotation.";
        else if (!canPlace) _placementWarning = "Blocked: space is occupied.";

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

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
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

    void OnChangeYLayer(InputAction.CallbackContext ctx)
    {
        if (!_isPlacing) return;

        float value = ctx.ReadValue<float>();
        if (Mathf.Abs(value) < 0.01f) return;

        bool usingGamepad = (InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad);

        if (usingGamepad)
        {
            // prevent super-fast repeats from held d-pad
            if (Time.time < _nextYLayerMoveTime) return;
            _nextYLayerMoveTime = Time.time + yLayerRepeatDelay;
        }

        int delta = value > 0f ? 1 : -1;

        activeLayerY = Mathf.Clamp(activeLayerY + delta, 0, grid.size.y - 1);
        _anchorCell.y = activeLayerY;
    }

    void OnDeletePiece(InputAction.CallbackContext _)
    {
        if (_isPlacing) return; // don't delete while actively placing/moving a piece
        TryReturnSelectedPieceToUI();
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
            _placedDefs.Remove(_restoreId);
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
        _nextYLayerMoveTime = 0f;

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

            grid.Remove(_restoreId);
            _placedDefs.Remove(_restoreId);
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

        _nextYLayerMoveTime = 0f;
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
        _nextYLayerMoveTime = 0f;

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

    public bool TryReturnSelectedPieceToUI()
    {
        PickupPiece pickup = null;

        bool usingGamepad = (InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad);

        if (usingGamepad)
        {
            pickup = _selected;
        }
        else
        {
            Vector2 screen = _player.Point.ReadValue<Vector2>();
            Ray r = cam.ScreenPointToRay(screen);

            if (Physics.Raycast(r, out RaycastHit hit, pickupMaxDistance, pickupMask))
                pickup = hit.collider.GetComponentInParent<PickupPiece>();
        }

        if (!pickup || !pickup.def) return false;
        if (pickup.placedId == 0) return false; // only already-placed pieces can be returned

        int placedId = pickup.placedId;
        PieceDefinition def = pickup.def;

        _restoreCells.Clear();
        if (!grid.TryGetPlacedCells(placedId, _restoreCells))
            return false;

        grid.Remove(placedId);
        _placedDefs.Remove(placedId);

        if (_selected == pickup)
        {
            ClearSelection();
            _selected = null;
            _targetIndex = -1;
        }

        Destroy(pickup.gameObject);

        OnReturned?.Invoke(def, placedId);
        return true;
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

    void OnGUI()
    {
        if (!_isPlacing) return;

        GUI.color = Color.white;
        GUI.Label(new Rect(12, 12, 200, 24), $"Layer Y: {activeLayerY}");

        if (!string.IsNullOrEmpty(_placementWarning))
        {
            GUI.color = Color.yellow;
            GUI.Label(new Rect(12, 36, 600, 24), _placementWarning);
        }
    }
}