using System.Collections.Generic;
using UnityEngine;

public class PackPuzzleValidator : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GridManager grid;
    [SerializeField] private PlacementController placement;
    [SerializeField] private PackManifest manifest;

    [Header("Debug")]
    [SerializeField] private bool logChecks = true;

    void Start()
    {
        if (!grid || !manifest)
        {
            Debug.LogError("PackPuzzleValidator is missing required references.");
            return;
        }

        if (!CanManifestTheoreticallyFillGridExactly())
        {
            Debug.LogError(
                $"Manifest volume ({GetManifestVolume()}) does not match grid volume ({grid.TotalCellCount})."
            );
        }
    }

    public bool IsSolved()
    {
        if (!grid || !placement || !manifest)
            return false;

        // 1) Every grid cell must be filled
        if (!grid.AreAllCellsOccupied())
        {
            if (logChecks)
                Debug.Log($"Puzzle not solved: grid not full ({grid.OccupiedCellCount}/{grid.TotalCellCount})");
            return false;
        }

        // 2) Every required piece count must match exactly
        Dictionary<PieceDefinition, int> placedCounts = placement.GetPlacedCounts();

        for (int i = 0; i < manifest.required.Count; i++)
        {
            PackRequirement req = manifest.required[i];
            if (req == null || req.def == null) continue;

            int placed = placedCounts.TryGetValue(req.def, out int value) ? value : 0;

            if (placed != req.count)
            {
                if (logChecks)
                    Debug.Log($"Puzzle not solved: {req.def.pieceName} requires {req.count}, placed {placed}");
                return false;
            }
        }

        // 3) No extra piece types beyond the manifest
        for (int i = 0; i < manifest.required.Count; i++)
        {
            PackRequirement req = manifest.required[i];
            if (req == null || req.def == null) continue;

            placedCounts.Remove(req.def);
        }

        foreach (var extra in placedCounts)
        {
            if (extra.Value > 0)
            {
                if (logChecks)
                    Debug.Log($"Puzzle not solved: extra piece used: {extra.Key.pieceName} x{extra.Value}");
                return false;
            }
        }

        if (logChecks)
            Debug.Log("Puzzle solved!");

        return true;
    }

    public bool CanManifestTheoreticallyFillGridExactly()
    {
        if (!grid || !manifest) return false;

        int manifestVolume = 0;

        for (int i = 0; i < manifest.required.Count; i++)
        {
            PackRequirement req = manifest.required[i];
            if (req == null || req.def == null) continue;
            if (req.def.occupiedCellsLocal == null) continue;

            manifestVolume += req.def.occupiedCellsLocal.Length * req.count;
        }

        return manifestVolume == grid.TotalCellCount;
    }

    public int GetManifestVolume()
    {
        if (!manifest) return 0;

        int manifestVolume = 0;

        for (int i = 0; i < manifest.required.Count; i++)
        {
            PackRequirement req = manifest.required[i];
            if (req == null || req.def == null) continue;
            if (req.def.occupiedCellsLocal == null) continue;

            manifestVolume += req.def.occupiedCellsLocal.Length * req.count;
        }

        return manifestVolume;
    }
}