using System.Collections.Generic;
using UnityEngine;

public class TruckLayoutGenerator : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GridManager grid;
    [SerializeField] private PieceLibrary pieceLibrary;

    [Header("Generation")]
    [SerializeField] private int maxAttempts = 200;
    [SerializeField] private bool randomizePieceOrder = true;

    [Header("Asset Output")]
    [SerializeField] private string manifestAssetName = "GeneratedPackManifest";
    [SerializeField] private string manifestSaveFolder = "Assets/_GameData/GeneratedManifests";

    private readonly List<PackedPieceRecord> _solution = new();
    private bool[,,] _filled;

    public IReadOnlyList<PackedPieceRecord> CurrentSolution => _solution;
    public string ManifestAssetName => manifestAssetName;
    public string ManifestSaveFolder => manifestSaveFolder;

    public bool GenerateFilledLayout()
    {
        if (!grid || !pieceLibrary || pieceLibrary.pieces.Count == 0)
        {
            Debug.LogError("Generator missing references.");
            return false;
        }

        bool success = false;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            _filled = new bool[grid.size.x, grid.size.y, grid.size.z];
            _solution.Clear();

            if (SolveRecursive())
            {
                success = true;
                break;
            }
        }

        Debug.Log(success
            ? $"Generated full layout with {_solution.Count} pieces."
            : $"Failed to generate a full layout after {maxAttempts} attempts.");

        return success;
    }

    bool SolveRecursive()
    {
        if (!TryFindFirstEmptyCell(out Vector3Int empty))
            return true;

        List<PieceDefinition> candidates = new List<PieceDefinition>(pieceLibrary.pieces);
        if (randomizePieceOrder)
            Shuffle(candidates);

        for (int i = 0; i < candidates.Count; i++)
        {
            PieceDefinition def = candidates[i];
            if (!def || def.occupiedCellsLocal == null || def.occupiedCellsLocal.Length == 0)
                continue;

            List<Quaternion> rotations = GetAllAxisAlignedRotations();
            if (randomizePieceOrder)
                Shuffle(rotations);

            for (int r = 0; r < rotations.Count; r++)
            {
                Quaternion rot = rotations[r];

                for (int j = 0; j < def.occupiedCellsLocal.Length; j++)
                {
                    Vector3Int localCell = def.occupiedCellsLocal[j];
                    Vector3Int pivot = def.pivotLocal;

                    Vector3 rel = localCell - pivot;
                    Vector3 rotated = rot * rel;

                    Vector3Int rotatedCell = new Vector3Int(
                        Mathf.RoundToInt(rotated.x),
                        Mathf.RoundToInt(rotated.y),
                        Mathf.RoundToInt(rotated.z)
                    );

                    Vector3Int anchor = empty - rotatedCell;

                    List<Vector3Int> worldCells = new List<Vector3Int>();
                    if (!ComputeWorldCells(def, anchor, rot, worldCells))
                        continue;

                    if (!CanFill(worldCells))
                        continue;

                    PlaceVirtual(def, anchor, rot, worldCells);

                    if (SolveRecursive())
                        return true;

                    RemoveVirtual(worldCells);
                    _solution.RemoveAt(_solution.Count - 1);
                }
            }
        }

        return false;
    }

    bool TryFindFirstEmptyCell(out Vector3Int cell)
    {
        for (int y = 0; y < grid.size.y; y++)
        {
            for (int z = 0; z < grid.size.z; z++)
            {
                for (int x = 0; x < grid.size.x; x++)
                {
                    if (!_filled[x, y, z])
                    {
                        cell = new Vector3Int(x, y, z);
                        return true;
                    }
                }
            }
        }

        cell = default;
        return false;
    }

    bool CanFill(List<Vector3Int> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int c = cells[i];

            if (c.x < 0 || c.y < 0 || c.z < 0 ||
                c.x >= grid.size.x || c.y >= grid.size.y || c.z >= grid.size.z)
                return false;

            if (_filled[c.x, c.y, c.z])
                return false;
        }

        return true;
    }

    void PlaceVirtual(PieceDefinition def, Vector3Int anchor, Quaternion rot, List<Vector3Int> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int c = cells[i];
            _filled[c.x, c.y, c.z] = true;
        }

        _solution.Add(new PackedPieceRecord
        {
            def = def,
            anchor = anchor,
            localRotation = rot,
            cells = cells.ToArray()
        });
    }

    void RemoveVirtual(List<Vector3Int> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int c = cells[i];
            _filled[c.x, c.y, c.z] = false;
        }
    }

    static bool ComputeWorldCells(PieceDefinition def, Vector3Int anchor, Quaternion rotLocal, List<Vector3Int> outCells)
    {
        outCells.Clear();

        if (def == null || def.occupiedCellsLocal == null || def.occupiedCellsLocal.Length == 0)
            return false;

        Vector3Int pivot = def.pivotLocal;

        for (int i = 0; i < def.occupiedCellsLocal.Length; i++)
        {
            Vector3Int local = def.occupiedCellsLocal[i];

            Vector3 rel = local - pivot;
            Vector3 rotatedRel = rotLocal * rel;

            Vector3Int rotatedCell = new Vector3Int(
                Mathf.RoundToInt(rotatedRel.x),
                Mathf.RoundToInt(rotatedRel.y),
                Mathf.RoundToInt(rotatedRel.z)
            );

            outCells.Add(anchor + rotatedCell);
        }

        return true;
    }

    static List<Quaternion> GetAllAxisAlignedRotations()
    {
        List<Quaternion> rotations = new List<Quaternion>();
        HashSet<string> seen = new HashSet<string>();

        int[] angles = { 0, 90, 180, 270 };

        for (int x = 0; x < angles.Length; x++)
        {
            for (int y = 0; y < angles.Length; y++)
            {
                for (int z = 0; z < angles.Length; z++)
                {
                    Quaternion q = Quaternion.Euler(angles[x], angles[y], angles[z]);

                    Vector3 right = SnapToAxis(q * Vector3.right);
                    Vector3 up = SnapToAxis(q * Vector3.up);
                    Vector3 forward = SnapToAxis(q * Vector3.forward);

                    string key = $"{right.x},{right.y},{right.z}|{up.x},{up.y},{up.z}|{forward.x},{forward.y},{forward.z}";
                    if (seen.Add(key))
                        rotations.Add(q);
                }
            }
        }

        return rotations;
    }

    static Vector3 SnapToAxis(Vector3 v)
    {
        return new Vector3(
            Mathf.Round(v.x),
            Mathf.Round(v.y),
            Mathf.Round(v.z)
        );
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }
    }

    public PackManifest BuildManifestFromSolution()
    {
        Dictionary<PieceDefinition, int> counts = new Dictionary<PieceDefinition, int>();

        for (int i = 0; i < _solution.Count; i++)
        {
            PieceDefinition def = _solution[i].def;
            if (!def) continue;

            if (!counts.ContainsKey(def))
                counts[def] = 0;

            counts[def]++;
        }

        PackManifest manifest = ScriptableObject.CreateInstance<PackManifest>();
        manifest.required = new List<PackRequirement>();

        foreach (var kvp in counts)
        {
            manifest.required.Add(new PackRequirement
            {
                def = kvp.Key,
                count = kvp.Value
            });
        }

        return manifest;
    }

    public void LogSolution()
    {
        if (_solution.Count == 0)
        {
            Debug.Log("No generated solution to log.");
            return;
        }

        Debug.Log($"Solution contains {_solution.Count} placed pieces:");

        for (int i = 0; i < _solution.Count; i++)
        {
            PackedPieceRecord p = _solution[i];
            Debug.Log($"[{i}] {p.def.pieceName} | Anchor: {p.anchor} | Cells: {p.cells.Length}");
        }
    }
}