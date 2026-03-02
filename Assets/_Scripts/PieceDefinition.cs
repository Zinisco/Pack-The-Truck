using UnityEngine;

public enum PieceWeight
{
    Light,
    Normal,
    Heavy
}

[CreateAssetMenu(menuName = "TruckPacker/Piece Definition")]
public class PieceDefinition : ScriptableObject
{
    public string pieceName;
    public GameObject visualPrefab;
    public Vector3Int[] occupiedCellsLocal;
    public Vector3Int pivotLocal = Vector3Int.zero;
    public Vector3Int standingBounds = new Vector3Int(1, 2, 1);

    [Header("Rules")]
    public bool fragileTop = false;
    public bool mustBeStanding = false;
    public bool forbidUpsideDown = false;

    [Header("Weight")]
    public PieceWeight weight = PieceWeight.Normal;

    [Header("Audio")]
    public AudioClip placeClipOverride;
    [Range(0f, 1f)] public float placeVolume = 1f;
    public Vector2 placePitchRange = new Vector2(0.95f, 1.05f);
}