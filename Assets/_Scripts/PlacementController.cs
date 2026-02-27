using System.Collections;
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

    [Header("Movement")]
    public float keyRepeatDelay = 0.18f;

    [Header("Layer")]
    [Range(0, 20)] public int activeLayerY = 0;
    public Key layerUpKey = Key.R;
    public Key layerDownKey = Key.F;

    [Header("Confirm / Undo")]
    public Key confirmKey = Key.Space;
    public Key undoKey = Key.Backspace;

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
        _player.Click.performed += OnClick;            // ONLY for pickup now
        _player.CancelPlacement.performed += OnCancelPlacement;

        RebuildGhost();
    }

    void OnDisable()
    {
        _player.Click.performed -= OnClick;
        _player.CancelPlacement.performed -= OnCancelPlacement;
        _player.Disable();
    }

    void Update()
    {
        if (!cam || !grid) return;

        // Undo anytime
        if (Keyboard.current != null && Keyboard.current[undoKey].wasPressedThisFrame)
            DoUndo();

        if (!_isPlacing) return;
        if (!currentDef) return;

        // Cancel placement
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ExitPlacementMode();
            return;
        }

        // Confirm placement (Space)
        if (Keyboard.current != null && Keyboard.current[confirmKey].wasPressedThisFrame)
        {
            if (TryPlaceHeld())
                return; // stop Update() this frame
        }

        // Layer up/down
        HandleLayerKeys();

        // Move
        StepMoveXZ(GetWASD());

        // Rotate (keyboard only)
        HandleRotationInputActions();

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

    void HandleLayerKeys()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[layerUpKey].wasPressedThisFrame)
            SetLayer(activeLayerY + 1);

        if (Keyboard.current[layerDownKey].wasPressedThisFrame)
            SetLayer(activeLayerY - 1);
    }

    void SetLayer(int y)
    {
        activeLayerY = Mathf.Clamp(y, 0, grid.size.y - 1);
        _anchorCell.y = activeLayerY;
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
        // Only rotate while placing
        if (!_isPlacing || currentDef == null) return;

        // Modifier held? (Shift in your bindings)
        bool negative = _player.RotationModifier.IsPressed();

        float degrees = negative ? -90f : 90f;

        // Use InputActions instead of Keyboard.current.xKey, etc.
        if (_player.RotateX.WasPressedThisFrame())
            RotateLocal(Vector3.right, degrees);

        if (_player.RotateY.WasPressedThisFrame())
            RotateLocal(Vector3.up, degrees);

        if (_player.RotateZ.WasPressedThisFrame())
            RotateLocal(Vector3.forward, degrees);
    }

    Vector2 GetWASD()
    {
        if (Keyboard.current == null) return Vector2.zero;

        float x = 0f;
        float y = 0f;

        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed) x += 1f;
        if (Keyboard.current.wKey.isPressed) y += 1f;
        if (Keyboard.current.sKey.isPressed) y -= 1f;

        Vector2 v = new Vector2(x, y);
        if (v.sqrMagnitude > 1f) v.Normalize();
        return v;
    }

    void StepMoveXZ(Vector2 move)
    {
        if (move == Vector2.zero) return;
        if (Time.time < _nextMoveTime) return;

        _nextMoveTime = Time.time + keyRepeatDelay;

        Vector3Int delta = new Vector3Int(
            Mathf.RoundToInt(move.x),
            0,
            Mathf.RoundToInt(move.y)
        );

        Vector3Int next = _anchorCell + delta;

        next.x = Mathf.Clamp(next.x, 0, grid.size.x - 1);
        next.y = Mathf.Clamp(next.y, 0, grid.size.y - 1);
        next.z = Mathf.Clamp(next.z, 0, grid.size.z - 1);

        _anchorCell = next;
        activeLayerY = _anchorCell.y;
    }

    void OnClick(InputAction.CallbackContext _)
    {
        // LMB is ONLY for picking up when not placing.
        // When placing, Spacebar confirms placement.
        if (_isPlacing) return;
        TryPickupFromScene();
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
        if (_holdingExistingPlaced)
        {
            grid.Remove(pickup.placedId);
            _placedVisualById.Remove(pickup.placedId);
            RemoveFromUndoStack(pickup.placedId);
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

        _placedVisualById[id] = placed;
        _undoStack.Push(id);

        if (!placed.activeSelf)
            placed.SetActive(true);

        PlayPlaceSfx();
        StartCoroutine(PopScale(placed));

        ExitPlacementMode();
        return true;
    }

    void ExitPlacementMode()
    {
        _isPlacing = false;
        grid.placementMode = false;

        if (_ghost) Destroy(_ghost);
        _ghost = null;
        _ghostRenderers = null;

        if (_heldPickup)
        {
            _heldPickup.gameObject.SetActive(true);
            _heldPickup = null;
        }

        currentDef = null;
        _rot = Quaternion.identity;
        _holdingExistingPlaced = false;
    }

    void OnCancelPlacement(InputAction.CallbackContext _)
    {
        if (!_isPlacing) return;
        ExitPlacementMode();
    }

    void DoUndo()
    {
        if (_undoStack.Count == 0) return;

        int id = _undoStack.Pop();
        grid.Remove(id);

        if (_placedVisualById.TryGetValue(id, out var go) && go)
            Destroy(go);

        _placedVisualById.Remove(id);
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

    void RemoveFromUndoStack(int id)
    {
        if (_undoStack.Count == 0) return;

        var temp = new Stack<int>();
        while (_undoStack.Count > 0)
        {
            int v = _undoStack.Pop();
            if (v != id) temp.Push(v);
        }
        while (temp.Count > 0)
            _undoStack.Push(temp.Pop());
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