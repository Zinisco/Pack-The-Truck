using UnityEngine;

[CreateAssetMenu(menuName = "TruckPacker/Piece Definition")]
public class PieceDefinition : ScriptableObject
{
    public string pieceName;
    public GameObject visualPrefab;
    public Vector3Int[] occupiedCellsLocal;
    public Vector3Int pivotLocal = Vector3Int.zero; // choose which local cell is the anchor/pivot

    [Header("Rules")]
    public bool fragileTop = false;
    public bool mustBeStanding = false;
    public bool forbidUpsideDown = false;

    [Header("Audio")]
    public AudioClip placeClipOverride;      // per-furniture sound (optional)
    [Range(0f, 1f)] public float placeVolume = 1f;
    public Vector2 placePitchRange = new Vector2(0.95f, 1.05f);
}