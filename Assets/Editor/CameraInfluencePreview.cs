using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

// Lets the Cm View show a node's Camera Influence camera as it will look with the boat in its box: the fog
// centred on the boat there (the middle of the box, on the water) at the influence's Fog Distance Override,
// and the follow camera's depth of field - the follow camera is the live one at runtime - focused on the
// influence's View Target. Put on for the one preview render and straight back, like the shot fog.
public static class CameraInfluencePreview
{
    static readonly int CentreId = Shader.PropertyToID("_WhiteFadeCentre");
    static readonly int RadiusId = Shader.PropertyToID("_WhiteFadeRadius");

    // The follow camera's post, looked up at most once a second, since the Cm View repaints every editor tick.
    static CinemachineVolumeSettings _followPost;
    static double _lookedUpAt = -1;

    // The influence on the watched camera, if it is a Camera Influence camera.
    public static LevelSelectCameraInfluence InfluenceOn(Object cam)
        => cam is Component c && c != null ? c.GetComponent<LevelSelectCameraInfluence>() : null;

    // The follow camera's Volume Settings: what the depth of field looks like with an influence in hold.
    public static CinemachineVolumeSettings FollowPost()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - _lookedUpAt < 1.0) return _followPost;

        _lookedUpAt = now;
        var follow  = Object.FindAnyObjectByType<LevelSelectCameraController>();
        _followPost = follow != null && follow.cam != null ? follow.cam.GetComponent<CinemachineVolumeSettings>() : null;
        return _followPost;
    }

    // The toolbar's summary of what the view wears.
    public static string Describe(LevelSelectCameraInfluence influence)
    {
        string distance = influence.overrideFogDistance ? $"distance {influence.fogDistance:0.#}" : "world distance";
        string focus    = influence.FocusTarget != null ? $", focus on {influence.FocusTarget.name}"
                        : FollowPost() == null ? ", no depth of field on the follow camera" : string.Empty;
        return $"{distance}{focus}";
    }

    // Puts the influence's fog on for a render. Hand the result to CameraSequenceFogPreview.Restore after it.
    public static CameraSequenceFogPreview.Saved Wear(LevelSelectCameraInfluence influence)
    {
        var saved = new CameraSequenceFogPreview.Saved
        {
            worn   = true,
            centre = Shader.GetGlobalVector(CentreId),
            radius = Shader.GetGlobalFloat(RadiusId),
        };

        Vector3 boat = influence.boxCentre;
        boat.y = CameraSequenceFogPreview.WaterLevel();
        Shader.SetGlobalVector(CentreId, boat);

        if (influence.overrideFogDistance)
            Shader.SetGlobalFloat(RadiusId, influence.fogDistance);

        return saved;
    }
}
