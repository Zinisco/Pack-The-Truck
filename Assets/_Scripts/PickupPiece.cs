using UnityEngine;

public class PickupPiece : MonoBehaviour
{
    public PieceDefinition def;

    // optional: if you want to hide the world object while placing
    public bool hideOnPickup = true;

    public int placedId;

    // optional: if you want the pickup to be clickable with raycasts
    // make sure it has a collider!
}