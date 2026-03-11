using UnityEngine;

[CreateAssetMenu(menuName = "3D Tetris/Piece Definition")]
public class PieceDefinition : ScriptableObject
{
    [Header("Identity")]
    public string pieceName;
    public Sprite icon;

    [Header("Visual")]
    public GameObject visualPrefab;
    public Vector3Int[] occupiedCellsLocal;
    public Vector3Int pivotLocal = Vector3Int.zero;

    [Header("Audio")]
    public AudioClip placeClipOverride;
    [Range(0f, 1f)] public float placeVolume = 1f;
    public Vector2 placePitchRange = new Vector2(0.95f, 1.05f);
}