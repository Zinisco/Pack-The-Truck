using UnityEngine;

[CreateAssetMenu(menuName = "TruckPacker/Piece Definition")]
public class PieceDefinition : ScriptableObject
{
    public string pieceName;
    public GameObject visualPrefab;          // nice furniture mesh/prefab
    public Vector3Int[] occupiedCellsLocal;  // local offsets from origin cell (0,0,0)
}