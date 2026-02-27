using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GridManager : MonoBehaviour
{
    public enum GridViewMode
    {
        Perspective,   // floor + two back walls
        Top2D,         // flat floor grid
        Bottom2D,      // same as Top2D (you can style later)
        Left2D,        // flat side grid
        Right2D,       // flat side grid
        Front2D,       // flat wall grid
        Back2D         // flat wall grid
    }

    [Header("Camera (for view-dependent planes)")]
    public Transform cameraTransform;

    [Header("Grid Size (cells)")]
    public Vector3Int size = new Vector3Int(6, 4, 10);

    [Header("Cell Size")]
    public float cellSize = 1f;

    [Header("Grid Origin")]
    public Transform origin;

    [Header("Grid Visual")]
    public bool showGridInPlacementMode = true;
    public bool placementMode = false;
    public float lineDuration = 0f;
    public Color gridColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("View")]
    public GridViewMode viewMode = GridViewMode.Perspective;

    bool[,,] _occ;
    readonly Dictionary<int, List<Vector3Int>> _placedCellsById = new();

    void Awake()
    {
        _occ = new bool[size.x, size.y, size.z];
        if (!origin) origin = transform;
    }

    // Call this from your camera preset script
    public void SetViewMode(GridViewMode mode) => viewMode = mode;

    void Update()
    {
        // Temporary toggle for placement mode
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            placementMode = !placementMode;

        if (!showGridInPlacementMode || !placementMode || !origin) return;

        DrawByMode(viewMode);
    }

    void DrawByMode(GridViewMode mode)
    {
        float sx = size.x * cellSize;
        float sy = size.y * cellSize;
        float sz = size.z * cellSize;

        switch (mode)
        {
            case GridViewMode.Top2D:
                DrawPlane_XZ(y: 0f, sx, sz);        // floor only
                break;

            case GridViewMode.Bottom2D:
                DrawPlane_XZ(y: sy, sx, sz);        // ceiling only (or keep 0f if you want)
                break;

            case GridViewMode.Left2D:
                DrawPlane_YZ(x: 0f, sy, sz);        // left wall only
                break;

            case GridViewMode.Right2D:
                DrawPlane_YZ(x: sx, sy, sz);        // right wall only
                break;

            case GridViewMode.Front2D:
                DrawPlane_XY(z: 0f, sx, sy);        // front wall only
                break;

            case GridViewMode.Back2D:
                DrawPlane_XY(z: sz, sx, sy);        // back wall only
                break;

            default:
                DrawPerspective_FloorAndBackWalls(sx, sy, sz);
                break;
        }
    }

    // Floor/ceiling style plane: XZ at fixed y
    void DrawPlane_XZ(float y, float sx, float sz)
    {
        // lines parallel to X (vary Z)
        for (int z = 0; z <= size.z; z++)
        {
            float zz = z * cellSize;
            Vector3 a = origin.TransformPoint(new Vector3(0f, y, zz));
            Vector3 b = origin.TransformPoint(new Vector3(sx, y, zz));
            Debug.DrawLine(a, b, gridColor, lineDuration);
        }

        // lines parallel to Z (vary X)
        for (int x = 0; x <= size.x; x++)
        {
            float xx = x * cellSize;
            Vector3 a = origin.TransformPoint(new Vector3(xx, y, 0f));
            Vector3 b = origin.TransformPoint(new Vector3(xx, y, sz));
            Debug.DrawLine(a, b, gridColor, lineDuration);
        }
    }

    // Wall plane: XY at fixed z
    void DrawPlane_XY(float z, float sx, float sy)
    {
        // lines parallel to X (vary Y)
        for (int y = 0; y <= size.y; y++)
        {
            float yy = y * cellSize;
            Vector3 a = origin.TransformPoint(new Vector3(0f, yy, z));
            Vector3 b = origin.TransformPoint(new Vector3(sx, yy, z));
            Debug.DrawLine(a, b, gridColor, lineDuration);
        }

        // lines parallel to Y (vary X)
        for (int x = 0; x <= size.x; x++)
        {
            float xx = x * cellSize;
            Vector3 a = origin.TransformPoint(new Vector3(xx, 0f, z));
            Vector3 b = origin.TransformPoint(new Vector3(xx, sy, z));
            Debug.DrawLine(a, b, gridColor, lineDuration);
        }
    }

    // Wall plane: YZ at fixed x
    void DrawPlane_YZ(float x, float sy, float sz)
    {
        // lines parallel to Y (vary Z)
        for (int z = 0; z <= size.z; z++)
        {
            float zz = z * cellSize;
            Vector3 a = origin.TransformPoint(new Vector3(x, 0f, zz));
            Vector3 b = origin.TransformPoint(new Vector3(x, sy, zz));
            Debug.DrawLine(a, b, gridColor, lineDuration);
        }

        // lines parallel to Z (vary Y)
        for (int y = 0; y <= size.y; y++)
        {
            float yy = y * cellSize;
            Vector3 a = origin.TransformPoint(new Vector3(x, yy, 0f));
            Vector3 b = origin.TransformPoint(new Vector3(x, yy, sz));
            Debug.DrawLine(a, b, gridColor, lineDuration);
        }
    }

    void DrawPerspective_FloorAndBackWalls(float sx, float sy, float sz)
    {
        // Always draw floor
        DrawPlane_XZ(y: 0f, sx, sz);

        // If we don't know the camera, fall back to a default pair
        if (!cameraTransform || !origin)
        {
            DrawPlane_XY(z: sz, sx, sy);
            DrawPlane_YZ(x: sx, sy, sz);
            return;
        }

        // Camera position in grid-local space
        Vector3 camLocal = origin.InverseTransformPoint(cameraTransform.position);

        // Box center in grid-local space
        float cx = sx * 0.5f;
        float cz = sz * 0.5f;

        // Pick the FAR walls (opposite the camera side)
        float backX = (camLocal.x < cx) ? sx : 0f; // camera on left => back wall is right
        float backZ = (camLocal.z < cz) ? sz : 0f; // camera near/front => back wall is far

        // Draw those two back walls
        DrawPlane_YZ(x: backX, sy, sz);
        DrawPlane_XY(z: backZ, sx, sy);
    }

    float NearestFaceX(float sx)
    {
        if (!cameraTransform || !origin) return 0f;
        float camX = origin.InverseTransformPoint(cameraTransform.position).x;
        return (camX <= sx * 0.5f) ? 0f : sx; // camera on left half => x=0 else x=sx
    }

    float NearestFaceZ(float sz)
    {
        if (!cameraTransform || !origin) return 0f;
        float camZ = origin.InverseTransformPoint(cameraTransform.position).z;
        return (camZ <= sz * 0.5f) ? 0f : sz; // camera on front half => z=0 else z=sz
    }

    float NearestFaceY(float sy)
    {
        if (!cameraTransform || !origin) return 0f;
        float camY = origin.InverseTransformPoint(cameraTransform.position).y;
        return (camY <= sy * 0.5f) ? 0f : sy; // camera on lower half => y=0 else y=sy
    }

    // --- your existing grid logic below unchanged ---
    public bool IsInside(Vector3Int c)
        => c.x >= 0 && c.y >= 0 && c.z >= 0 && c.x < size.x && c.y < size.y && c.z < size.z;

    public bool IsFree(Vector3Int c)
        => IsInside(c) && !_occ[c.x, c.y, c.z];

    public Vector3 CellToWorldCenter(Vector3Int c)
    {
        Vector3 local = new Vector3((c.x + 0.5f) * cellSize, (c.y + 0.5f) * cellSize, (c.z + 0.5f) * cellSize);
        return origin.TransformPoint(local);
    }

    public Vector3Int WorldToCell(Vector3 world)
    {
        Vector3 local = origin.InverseTransformPoint(world);
        int x = Mathf.FloorToInt(local.x / cellSize);
        int y = Mathf.FloorToInt(local.y / cellSize);
        int z = Mathf.FloorToInt(local.z / cellSize);
        return new Vector3Int(x, y, z);
    }

    public Vector3 GetWorldCeilingCenter()
    {
        if (!origin) origin = transform;
        float sx = size.x * cellSize;
        float sy = size.y * cellSize;
        float sz = size.z * cellSize;
        return origin.TransformPoint(new Vector3(sx * 0.5f, sy, sz * 0.5f));
    }

    public Vector3 GetWorldLeftWallCenter()
    {
        if (!origin) origin = transform;
        float sy = size.y * cellSize;
        float sz = size.z * cellSize;
        return origin.TransformPoint(new Vector3(0f, sy * 0.5f, sz * 0.5f));
    }

    public Vector3 GetWorldRightWallCenter()
    {
        if (!origin) origin = transform;
        float sx = size.x * cellSize;
        float sy = size.y * cellSize;
        float sz = size.z * cellSize;
        return origin.TransformPoint(new Vector3(sx, sy * 0.5f, sz * 0.5f));
    }

    public Vector3 GetWorldFrontWallCenter()
    {
        if (!origin) origin = transform;
        float sx = size.x * cellSize;
        float sy = size.y * cellSize;
        return origin.TransformPoint(new Vector3(sx * 0.5f, sy * 0.5f, 0f));
    }

    public Vector3 GetWorldBackWallCenter()
    {
        if (!origin) origin = transform;
        float sx = size.x * cellSize;
        float sy = size.y * cellSize;
        float sz = size.z * cellSize;
        return origin.TransformPoint(new Vector3(sx * 0.5f, sy * 0.5f, sz));
    }

    public bool CanPlaceCells(IReadOnlyList<Vector3Int> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (!IsInside(c)) return false;
            if (_occ[c.x, c.y, c.z]) return false;
        }
        return true;
    }

    public void Place(int placedId, IReadOnlyList<Vector3Int> cells)
    {
        var list = new List<Vector3Int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            _occ[c.x, c.y, c.z] = true;
            list.Add(c);
        }
        _placedCellsById[placedId] = list;
    }

    public bool Remove(int placedId)
    {
        if (!_placedCellsById.TryGetValue(placedId, out var cells)) return false;

        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (IsInside(c)) _occ[c.x, c.y, c.z] = false;
        }

        _placedCellsById.Remove(placedId);
        return true;
    }

    public Vector3 GetWorldCenter()
    {
        if (!origin) origin = transform;

        float sx = size.x * cellSize;
        float sy = size.y * cellSize;
        float sz = size.z * cellSize;

        Vector3 localCenter = new Vector3(sx, sy, sz) * 0.5f;
        return origin.TransformPoint(localCenter);
    }

    public Vector3 GetWorldFloorCenter()
    {
        if (!origin) origin = transform;

        float sx = size.x * cellSize;
        float sz = size.z * cellSize;

        // center of floor (y = 0)
        Vector3 local = new Vector3(sx * 0.5f, 0f, sz * 0.5f);
        return origin.TransformPoint(local);
    }

    // layerIndex: 0..size.y-1 (cell layers)
    // useCellCenter = true -> centers on the middle of that cell layer (nice for “slice” viewing)
    public Vector3 GetWorldLayerCenter(int layerIndex, bool useCellCenter = true)
    {
        if (!origin) origin = transform;

        float sx = size.x * cellSize;
        float sz = size.z * cellSize;

        float y = useCellCenter
            ? (layerIndex + 0.5f) * cellSize
            : layerIndex * cellSize;

        Vector3 local = new Vector3(sx * 0.5f, y, sz * 0.5f);
        return origin.TransformPoint(local);
    }
}