using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PackRequirement
{
    public PieceDefinition def;
    [Min(1)] public int count = 1;
}

[CreateAssetMenu(menuName = "TruckPacker/Pack Manifest")]
public class PackManifest : ScriptableObject
{
    public List<PackRequirement> required = new List<PackRequirement>();
}