using UnityEditor;
using UnityEngine;

// Lets the Cm View show a Camera Sequence's fog: when the watched camera is one of a sequence's shot
// cameras and that sequence has Override Fog on, its White Fade numbers (and the shot's Fog Centre, from
// the camera as it stands) are put on the globals for the one preview render, then the world's
// are put straight back - the Scene view and the game never see them.
public static class CameraSequenceFogPreview
{
    static readonly int RadiusId         = Shader.PropertyToID("_WhiteFadeRadius");
    static readonly int OuterOpacityId   = Shader.PropertyToID("_WhiteFadeOuterOpacity");
    static readonly int InnerDivisionId  = Shader.PropertyToID("_WhiteFadeInnerDivision");
    static readonly int InnerOpacityId   = Shader.PropertyToID("_WhiteFadeInnerOpacity");
    static readonly int GradientBottomId = Shader.PropertyToID("_WhiteFadeGradientBottom");
    static readonly int CentreId         = Shader.PropertyToID("_WhiteFadeCentre");
    static readonly int BoatCentreId     = Shader.PropertyToID("_BoatWorldCenter");

    // The world's fog as it stood before a preview render, to put back after it.
    public struct Saved
    {
        public bool    worn;
        public float   radius, outerOpacity, innerDivision, innerOpacity, gradientBottom;
        public Vector4 centre;
    }

    // Which sequence a camera's fog comes from - looked up at most once a second, since the Cm View
    // repaints every editor tick.
    static Object _lookedUpFor;
    static CameraSequence _found;
    static LevelSelectBoatControl _boat;
    static double _lookedUpAt = -1;

    // The first sequence in the open scenes with Override Fog on that uses this camera for a shot.
    public static CameraSequence SequenceFor(Object cam)
    {
        if (cam == null) return null;

        double now = EditorApplication.timeSinceStartup;
        if (ReferenceEquals(_lookedUpFor, cam) && now - _lookedUpAt < 1.0) return _found;

        _lookedUpFor = cam;
        _lookedUpAt  = now;
        _found       = null;
        _boat        = Object.FindAnyObjectByType<LevelSelectBoatControl>();

        foreach (var sequence in Object.FindObjectsByType<CameraSequence>(FindObjectsInactive.Include,
                                                                          FindObjectsSortMode.None))
        {
            if (!sequence.overrideFog) continue;
            foreach (var shot in sequence.shots)
                if ((shot.camera != null && shot.camera == cam) || (shot.endCamera != null && shot.endCamera == cam))
                {
                    _found = sequence;
                    return _found;
                }
        }
        return null;
    }

    // Puts the sequence's fog on for a render from previewCam. Hand the result to Restore after it.
    // The Fog Centre of the first shot that uses this camera (an end camera counts for its shot).
    public static CameraSequence.FogCentre CentreFor(CameraSequence sequence, Object cam)
    {
        foreach (var shot in sequence.shots)
            if ((shot.camera != null && shot.camera == cam) || (shot.endCamera != null && shot.endCamera == cam))
                return shot.fogCentre;
        return CameraSequence.FogCentre.View;
    }

    public static Saved Wear(CameraSequence sequence, Object watched, Camera previewCam)
    {
        var saved = new Saved
        {
            worn           = true,
            radius         = Shader.GetGlobalFloat(RadiusId),
            outerOpacity   = Shader.GetGlobalFloat(OuterOpacityId),
            innerDivision  = Shader.GetGlobalFloat(InnerDivisionId),
            innerOpacity   = Shader.GetGlobalFloat(InnerOpacityId),
            gradientBottom = Shader.GetGlobalFloat(GradientBottomId),
            centre         = Shader.GetGlobalVector(CentreId),
        };

        sequence.fog.PushNow();

        // Boat, or a view looking level or up that never meets the water: the fog stays on the boat.
        if (CameraSequence.FogSpot(CentreFor(sequence, watched), previewCam.transform, WaterLevel(), out var spot))
            Shader.SetGlobalVector(CentreId, spot);

        return saved;
    }

    public static void Restore(Saved saved)
    {
        if (!saved.worn) return;

        Shader.SetGlobalFloat(RadiusId,         saved.radius);
        Shader.SetGlobalFloat(OuterOpacityId,   saved.outerOpacity);
        Shader.SetGlobalFloat(InnerDivisionId,  saved.innerDivision);
        Shader.SetGlobalFloat(InnerOpacityId,   saved.innerOpacity);
        Shader.SetGlobalFloat(GradientBottomId, saved.gradientBottom);
        Shader.SetGlobalVector(CentreId,        saved.centre);
    }

    // The boat's height, as the game takes it. In edit mode nothing pushes _BoatWorldCenter every frame,
    // so the boat in the scene is asked first.
    static float WaterLevel()
    {
        if (_boat != null)
        {
            var visual = _boat.MeshTransform != null ? _boat.MeshTransform : _boat.BoatTransform;
            if (visual != null) return visual.position.y;
        }
        return Shader.GetGlobalVector(BoatCentreId).y;
    }
}
