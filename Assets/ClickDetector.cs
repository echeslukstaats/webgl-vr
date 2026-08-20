using UnityEngine;

/// <summary>
/// Planet picking is handled by UI buttons layered over the map image (see
/// <see cref="SystemMapUI"/>), so no physics raycasting is needed any more.
///
/// This component is kept only so the existing GameObject in the scene does
/// not report a missing script; it is safe to delete both this file and that
/// component together.
/// </summary>
public class ClickDetector : MonoBehaviour {
    [SerializeField] private SystemMapUI systemMapUI;
}
