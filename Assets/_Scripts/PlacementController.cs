using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementController : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public GridManager grid;

    [Header("Piece Selection")]
    public PieceDefinition currentDef;

    [Header("Ghost Visuals")]
    public Material validMat;
    public Material invalidMat;

    [Header("Locking")]
    public bool freezePlacementWhileOrbiting = true;   // don’t move when orbiting camera
    public bool preserveXZOnLayerChange = true;        // scroll changes only Y

    int _skipPointerFrames = 0; // prevents one-frame “jump” after layer change

    [Header("Pickup")]
    public LayerMask pickupMask;          // set this to your "Pickup" layer
    public float pickupMaxDistance = 100f;

    bool _isPlacing = false;
    PickupPiece _heldPickup;              // the scene object we clicked (optional)

    [Header("Layer")]
    [Range(0, 20)] public int activeLayerY = 0;
    public float scrollToLayerThreshold = 0.25f; // scroll is a Vector2; we use y

    InputSystem_Actions _input;
    InputSystem_Actions.PlayerActions _player;

    GameObject _ghost;
    Renderer[] _ghostRenderers;

    Quaternion _rot = Quaternion.identity;
    Vector3Int _anchorCell;
    Vector3 _ghostVisualCenterLocal;   // local-space point that represents the visual "center"

    int _nextPlacedId = 1;

    // Undo stack holds placed ids. Dict stores the spawned visual per id.
    readonly Stack<int> _undoStack = new();
    readonly Dictionary<int, GameObject> _placedVisualById = new();

    readonly List<Vector3Int> _tmpWorldCells = new();

    void Awake()
    {
        _input = new InputSystem_Actions();
        _player = _input.Player;
    }

    void OnEnable()
    {
        _player.Enable();

        _player.Click.performed += OnClick;
        _player.Undo.performed += OnUndo;

        _player.YawLeft.performed += _ => RotateYaw(-90);
        _player.YawRight.performed += _ => RotateYaw(90);
        _player.PitchUp.performed += _ => RotatePitch(-90);
        _player.PitchDown.performed += _ => RotatePitch(90);
        _player.RollLeft.performed += _ => RotateRoll(-90);
        _player.RollRight.performed += _ => RotateRoll(90);

        RebuildGhost();
    }

    void OnDisable()
    {
        _player.Click.performed -= OnClick;
        _player.Undo.performed -= OnUndo;

        _player.Disable();
    }

    void Update()
    {
        if (!cam || !grid) return;

        if (!_isPlacing)
            return; // not holding anything, grid stays off, ghost doesn't update

        if (!currentDef) return;

        HandleLayerScroll();

        bool orbiting = freezePlacementWhileOrbiting && _player.OrbitHold.IsPressed();

        if (_skipPointerFrames > 0)
        {
            _skipPointerFrames--;
        }
        else if (!orbiting)
        {
            UpdateAnchorFromPointer();
        }

        bool canPlace =
            ComputeWorldCells(currentDef, _anchorCell, _rot, _tmpWorldCells) &&
            grid.CanPlaceCells(_tmpWorldCells);

        UpdateGhostTransform();
        SetGhostMaterial(canPlace ? validMat : invalidMat);
    }

    void HandleLayerScroll()
    {
        // No layer changes in 2D modes
        if (grid && grid.viewMode != GridManager.GridViewMode.Perspective)
            return;

        Vector2 scroll = _player.LayerDelta.ReadValue<Vector2>();
        if (Mathf.Abs(scroll.y) < scrollToLayerThreshold) return;

        int step = scroll.y > 0 ? 1 : -1;
        SetLayer(activeLayerY + step);
    }

    void SetLayer(int y)
    {
        activeLayerY = Mathf.Clamp(y, 0, grid.size.y - 1);
        _anchorCell.y = activeLayerY;
    }

    void RebuildGhost()
    {
        if (_ghost) Destroy(_ghost);

        if (!currentDef || !currentDef.visualPrefab) return;

        _ghost = Instantiate(currentDef.visualPrefab);
        _ghost.name = $"Ghost_{currentDef.pieceName}";
        _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>(true);

        // Cache a "visual center" point in LOCAL space so we can align it to grid centers.
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

        // Move the object so its visual center sits on the target center.
        _ghost.transform.position = targetCenterWorld - (_ghost.transform.rotation * _ghostVisualCenterLocal);
    }

    void UpdateAnchorFromPointer()
    {
        Vector2 screen = _player.Point.ReadValue<Vector2>();
        Ray r = cam.ScreenPointToRay(screen);

        var mode = grid ? grid.viewMode : GridManager.GridViewMode.Perspective;

        Plane p;

        // Which axis is fixed in this view? (and what fixed cell index?)
        bool lockX = false, lockY = false, lockZ = false;
        int fixedX = 0, fixedY = 0, fixedZ = 0;

        float sx = grid.size.x * grid.cellSize;
        float sy = grid.size.y * grid.cellSize;
        float sz = grid.size.z * grid.cellSize;

        switch (mode)
        {
            case GridManager.GridViewMode.Top2D:
                // Place on floor (y = 0)
                p = new Plane(grid.origin.up, grid.origin.TransformPoint(new Vector3(0f, 0f, 0f)));
                lockY = true; fixedY = 0;
                break;

            case GridManager.GridViewMode.Bottom2D:
                // Place on ceiling layer (y = size.y-1)
                p = new Plane(grid.origin.up, grid.origin.TransformPoint(new Vector3(0f, sy, 0f)));
                lockY = true; fixedY = grid.size.y - 1;
                break;

            case GridManager.GridViewMode.Left2D:
                // Place on left wall (x = 0)
                p = new Plane(grid.origin.right, grid.origin.TransformPoint(new Vector3(0f, 0f, 0f)));
                lockX = true; fixedX = 0;
                break;

            case GridManager.GridViewMode.Right2D:
                // Place on right wall (x = size.x-1)
                p = new Plane(grid.origin.right, grid.origin.TransformPoint(new Vector3(sx, 0f, 0f)));
                lockX = true; fixedX = grid.size.x - 1;
                break;

            case GridManager.GridViewMode.Front2D:
                // Place on front wall (z = 0)
                p = new Plane(grid.origin.forward, grid.origin.TransformPoint(new Vector3(0f, 0f, 0f)));
                lockZ = true; fixedZ = 0;
                break;

            case GridManager.GridViewMode.Back2D:
                // Place on back wall (z = size.z-1)
                p = new Plane(grid.origin.forward, grid.origin.TransformPoint(new Vector3(0f, 0f, sz)));
                lockZ = true; fixedZ = grid.size.z - 1;
                break;

            default:
                {
                    // Perspective (your existing behavior)
                    int y = Mathf.Clamp(activeLayerY, 0, grid.size.y - 1);

                    if (preserveXZOnLayerChange)
                    {
                        // Fixed plane at floor, then apply Y
                        Vector3 floorPoint = grid.origin.TransformPoint(new Vector3(0f, 0f, 0f));
                        p = new Plane(grid.origin.up, floorPoint);
                    }
                    else
                    {
                        Vector3 planePoint = grid.origin.TransformPoint(new Vector3(0f, y * grid.cellSize, 0f));
                        p = new Plane(grid.origin.up, planePoint);
                    }

                    if (!p.Raycast(r, out float enter0)) return;

                    Vector3 hit0 = r.GetPoint(enter0);
                    Vector3Int cell0 = grid.WorldToCell(hit0);

                    cell0.x = Mathf.Clamp(cell0.x, 0, grid.size.x - 1);
                    cell0.z = Mathf.Clamp(cell0.z, 0, grid.size.z - 1);
                    cell0.y = y;

                    _anchorCell = cell0;
                    return;
                }
        }

        if (!p.Raycast(r, out float enter))
            return;

        Vector3 hit = r.GetPoint(enter);
        Vector3Int cell = grid.WorldToCell(hit);

        // Clamp first
        cell.x = Mathf.Clamp(cell.x, 0, grid.size.x - 1);
        cell.y = Mathf.Clamp(cell.y, 0, grid.size.y - 1);
        cell.z = Mathf.Clamp(cell.z, 0, grid.size.z - 1);

        // Then enforce the locked axis for the 2D face
        if (lockX) cell.x = fixedX;
        if (lockY) cell.y = fixedY;
        if (lockZ) cell.z = fixedZ;

        _anchorCell = cell;

        // Keep activeLayerY synced in case you return to perspective
        activeLayerY = _anchorCell.y;
    }

    static bool ComputeWorldCells(PieceDefinition def, Vector3Int anchor, Quaternion rotLocal, List<Vector3Int> outCells)
    {
        outCells.Clear();
        if (def == null || def.occupiedCellsLocal == null) return false;

        for (int i = 0; i < def.occupiedCellsLocal.Length; i++)
        {
            Vector3Int local = def.occupiedCellsLocal[i];

            Vector3 rotated = rotLocal * (Vector3)local;
            var r = new Vector3Int(
                Mathf.RoundToInt(rotated.x),
                Mathf.RoundToInt(rotated.y),
                Mathf.RoundToInt(rotated.z)
            );

            outCells.Add(anchor + r);
        }

        return true;
    }

    void OnClick(InputAction.CallbackContext _)
    {
        if (!_isPlacing)
        {
            TryPickupFromScene();
            return;
        }

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

        // Enter placement mode
        _heldPickup = pickup;
        SetCurrentPiece(pickup.def);      // sets currentDef + resets rot + rebuilds ghost

        grid.placementMode = true;        // toggles grid draw
        _isPlacing = true;

        // Optional: hide the world object while placing
        if (pickup.hideOnPickup)
            pickup.gameObject.SetActive(false);
    }

    void TryPlaceHeld()
    {
        if (!currentDef) return;

        if (!ComputeWorldCells(currentDef, _anchorCell, _rot, _tmpWorldCells)) return;
        if (!grid.CanPlaceCells(_tmpWorldCells)) return;

        int id = _nextPlacedId++;

        var placed = Instantiate(currentDef.visualPrefab);
        placed.name = $"Placed_{currentDef.pieceName}_{id}";
        placed.transform.rotation = grid.origin.rotation * _rot;

        Vector3 targetCenterWorld = ComputeWorldBoundsCenter(_tmpWorldCells);

        // compute visual center local for this instance
        Vector3 visualCenterLocal = Vector3.zero;
        if (TryGetRendererBounds(placed, out var wb))
            visualCenterLocal = placed.transform.InverseTransformPoint(wb.center);

        placed.transform.position = targetCenterWorld - (placed.transform.rotation * visualCenterLocal);

        grid.Place(id, _tmpWorldCells);
        _placedVisualById[id] = placed;
        _undoStack.Push(id);

        ExitPlacementMode(destroyPickup: true);
    }

    void ExitPlacementMode(bool destroyPickup)
    {
        _isPlacing = false;
        grid.placementMode = false;

        if (_ghost) Destroy(_ghost);
        _ghost = null;
        _ghostRenderers = null;

        if (_heldPickup)
        {
            if (destroyPickup)
                Destroy(_heldPickup.gameObject);   // remove the original scene couch
            else
                _heldPickup.gameObject.SetActive(true);

            _heldPickup = null;
        }

        currentDef = null;
        _rot = Quaternion.identity;
    }

    void OnUndo(InputAction.CallbackContext _)
    {
        if (_undoStack.Count == 0) return;

        int id = _undoStack.Pop();

        grid.Remove(id);

        if (_placedVisualById.TryGetValue(id, out var go) && go)
            Destroy(go);

        _placedVisualById.Remove(id);
    }

    void OnCancelPlacemet(InputAction.CallbackContext _)
    {
        if (!_isPlacing) return;
        ExitPlacementMode(destroyPickup: true);
    }

    void RotateYaw(int degrees) => _rot = Quaternion.Euler(0, degrees, 0) * _rot;
    void RotatePitch(int degrees) => _rot = Quaternion.Euler(degrees, 0, 0) * _rot;
    void RotateRoll(int degrees) => _rot = Quaternion.Euler(0, 0, degrees) * _rot;

    public void SetCurrentPiece(PieceDefinition def)
    {
        currentDef = def;
        _rot = Quaternion.identity;
        RebuildGhost();
    }

    Vector3 ComputeWorldBoundsCenter(IReadOnlyList<Vector3Int> worldCells)
    {
        // Convert each occupied cell center to world, then take bounds
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
}