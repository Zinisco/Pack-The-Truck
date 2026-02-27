using UnityEngine;

[CreateAssetMenu(menuName = "TruckPacker/Piece Definition")]
public class PieceDefinition : ScriptableObject
{
    public string pieceName;
    public GameObject visualPrefab;
    public Vector3Int[] occupiedCellsLocal;

    [Header("Audio")]
    public AudioClip placeClipOverride;      // per-furniture sound (optional)
    [Range(0f, 1f)] public float placeVolume = 1f;
    public Vector2 placePitchRange = new Vector2(0.95f, 1.05f);
}