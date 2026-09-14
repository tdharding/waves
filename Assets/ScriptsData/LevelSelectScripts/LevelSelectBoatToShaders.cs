using UnityEngine;

/// <summary>
/// Pushes the level select boat's position out as _BoatWorldCenter, the global every
/// boat-centred shader effect measures from.
///
/// Inside a level BoatToWaterMaterial does this, among a great many other things it does for
/// the water, the arena mask and the camera zoom — none of which exist on the map. This is the
/// one line of that job the map needs, on its own.
///
/// It is not optional decoration. Shader globals outlive a scene load, so with nothing pushing
/// out here the darkness mask and the boat light stay pinned to wherever the arena boat was
/// standing when the player left the level — frozen, but lit, which reads as working. Coming
/// in cold they sit on the world origin instead.
///
/// Read by RiverRunShader, ArenaWallsShader and the spline wall graphs, by the boat light in
/// InstancedLights.hlsl, and as a fallback by RockRingManager and FogFieldManager when neither
/// has been handed a boat of its own.
/// </summary>
public class LevelSelectBoatToShaders : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The boat on the map. Left empty, it is found at load.")]
    [SerializeField] private LevelSelectBoatControl boatControl;

    static readonly int BoatCentreId = Shader.PropertyToID("_BoatWorldCenter");

    private bool _warned;

    private void Awake()
    {
        if (boatControl == null)
            boatControl = FindObjectOfType<LevelSelectBoatControl>();
    }

    /// <summary>
    /// In LateUpdate, and deliberately. The boat is driven in FixedUpdate on an interpolated
    /// body, so its transform only holds the position it is actually drawn at once the frame's
    /// movement is done. Pushing any earlier hands the shaders last step's boat.
    /// </summary>
    private void LateUpdate()
    {
        if (boatControl == null)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[LevelSelectBoatToShaders] No LevelSelectBoatControl in the " +
                                 "scene, so nothing pushes _BoatWorldCenter and every " +
                                 "boat-centred effect keeps whatever the last scene left in it.",
                                 this);
            }
            return;
        }

        // The hull, not the physics transform — the same choice BoatToWaterMaterial makes in
        // taking the boat VISUAL, so the light sits on the boat that is drawn rather than half
        // a metre off it. Falls back to the body for a boat with no separate mesh.
        Transform visual = boatControl.MeshTransform != null
            ? boatControl.MeshTransform
            : boatControl.BoatTransform;

        if (visual == null) return;

        Shader.SetGlobalVector(BoatCentreId, visual.position);
    }
}
