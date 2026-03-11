using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PackedPieceRecord
{
    public PieceDefinition def;
    public Vector3Int anchor;
    public Vector3Int[] cells;
    public Quaternion localRotation;
}