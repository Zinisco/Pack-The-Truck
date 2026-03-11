using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "3D Tetris/Piece Library")]
public class PieceLibrary : ScriptableObject
{
    public List<PieceDefinition> pieces = new List<PieceDefinition>();
}