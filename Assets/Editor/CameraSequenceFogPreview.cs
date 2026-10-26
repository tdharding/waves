using UnityEditor;
using UnityEngine;

// Lets the Cm View show a Camera Sequence's fog: when the watched camera is one of a sequence's shot
// cameras, the shot's Fog Centre (from the camera as it stands) and its Fog Distance Override are put on
// the globals for the one preview render, then the world's are put straight back - the Scene view and the
// game never see them.
public static class CameraSequenceFogPreview
{
    static readonly int CentreId         = Shader.PropertyToID("_WhiteFadeCentre");
    static readonly int RadiusId         = Shader.PropertyToID("_WhiteFadeRadius");
    static readonly int BoatCentreId     = Shader.PropertyToID("_BoatWorldCenter");

    // The world's fog as it stood before a preview render, to put back after it.
    public struct Saved
    {
        public bool    worn;
        public Vector4 centre;
        public float   radius;
    }

    // Which sequence a camera's fog comes from - looked up at most once a second, since the Cm View
    // repaints every editor tick.
    static Object _lookedUpFor;
    static CameraSequence _found;
    static LevelSelectBoatControl _boat;
    static double _lookedUpAt = -1;

    // The first sequence in the open scenes that uses this camera for a shot.
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
            foreach (var shot in sequence.shots)
                if (Uses(shot, cam))
                {
                    _found = sequence;
                    return _found;
                }
        }
        return null;
    }

    // An end camera counts for its shot.
    static bool Uses(CameraSequence.Shot shot, Object cam)
        => (shot.camera != null && shot.camera == cam) || (shot.endCamera != null && shot.endCamera == cam);

    // Which shot's fog this camera wears: the one asked for (the shot the Cm View shows) when it uses the
    // camera, else the first shot that does. -1 for none.
    public static int ShotIndexFor(CameraSequence sequence, Object cam, int preferred = -1)
    {
        if (sequence == null || cam == null) return -1;
        if (preferred >= 0 && preferred < sequence.shots.Count && Uses(sequence.shots[preferred], cam)) return preferred;
        return sequence.shots.FindIndex(shot => Uses(shot, cam));
    }

    // What the shot's depth of field focuses on, as it will at runtime: its View Target with Focus On View
    // Target on, else nothing (the camera's own focus).
    public static Transform FocusFor(CameraSequence.Shot shot)
        => shot != null && shot.focusViewTarget && shot.viewTarget != null ? shot.viewTarget : null;

    // The toolbar's summary of what the view wears.
    public static string Describe(CameraSequence.Shot shot)
    {
        string distance = shot.overrideFogDistance ? $"distance {shot.fogDistance:0.#}" : "world distance";
        string focus    = FocusFor(shot) != null ? $", focus on {shot.viewTarget.name}" : string.Empty;
        return $"{shot.fogCentre}, {distance}{focus}";
    }

    // Puts the shot's fog on for a render from previewCam. Hand the result to Restore after it.
    public static Saved Wear(CameraSequence.Shot shot, Camera previewCam)
    {
        var saved = new Saved
        {
            worn   = true,
            centre = Shader.GetGlobalVector(CentreId),
            radius = Shader.GetGlobalFloat(RadiusId),
        };

        // Boat, or a view looking level or up that never meets the water: the fog stays on the boat.
        if (CameraSequence.FogSpot(shot.fogCentre, previewCam.transform, WaterLevel(), out var spot))
            Shader.SetGlobalVector(CentreId, spot);

        if (shot.overrideFogDistance)
            Shader.SetGlobalFloat(RadiusId, shot.fogDistance);

        return saved;
    }

    public static void Restore(Saved saved)
    {
        if (!saved.worn) return;

        Shader.SetGlobalVector(CentreId, saved.centre);
        Shader.SetGlobalFloat(RadiusId, saved.radius);
    }

    // The boat's height, as the game takes it. In edit mode nothing pushes _BoatWorldCenter every frame,
    // so the boat in the scene is asked first.
    public static float WaterLevel()
    {
        if (_boat != null)
        {
            var visual = _boat.MeshTransform != null ? _boat.MeshTransform : _boat.BoatTransform;
            if (visual != null) return visual.position.y;
        }
        return Shader.GetGlobalVector(BoatCentreId).y;
    }
}
