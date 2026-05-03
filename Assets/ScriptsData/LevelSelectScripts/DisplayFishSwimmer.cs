using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Animates a display fish along a generated <see cref="SplineContainer"/> path.
/// Uses <see cref="SplineUtility.Evaluate"/> to sample position and tangent so the
/// fish follows the curve smoothly and always faces its direction of travel.
///
/// [ExecuteAlways] lets it run in prefab edit mode for live preview.
/// Call <see cref="Initialise"/> immediately after instantiation.
/// </summary>
[ExecuteAlways]
public class DisplayFishSwimmer : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────

    private SplineContainer _splinePath;
    private float           _t;             // Normalised progress 0–1 along the spline
    private float           _swimSpeed;     // Units per second along the path
    private float           _turnSpeed;     // Degrees per second toward travel direction
    private bool            _initialised;
    private float           _lastRealTime = -1f;

    // ─────────────────────────────────────────────
    // INITIALISATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="LevelSelectSoulFishDisplay"/> immediately after spawning.
    /// </summary>
    public void Initialise(SplineContainer splinePath, float swimSpeed, float turnSpeed)
    {
        _splinePath   = splinePath;
        _swimSpeed    = swimSpeed;
        _turnSpeed    = turnSpeed;
        _t            = UnityEngine.Random.Range(0f, 1f); // stagger start positions around the path
        _initialised  = true;
        _lastRealTime = -1f;
    }

    // ─────────────────────────────────────────────
    // UPDATE
    // ─────────────────────────────────────────────

    private void Update()
    {
        if (!_initialised || _splinePath == null) return;

        float realNow  = Time.realtimeSinceStartup;
        float delta    = _lastRealTime < 0f ? 0f : Mathf.Min(realNow - _lastRealTime, 0.05f);
        _lastRealTime  = realNow;

        // Advance t — divide by spline length so swimSpeed is in world units/sec
        float length = _splinePath.Spline.GetLength();
        if (length > 0.001f)
            _t = (_t + _swimSpeed * delta / length) % 1f;

        // Sample position and tangent in spline local space, then convert to world
        SplineUtility.Evaluate(_splinePath.Spline, _t,
            out float3 localPos, out float3 localTan, out float3 _);

        Vector3 worldPos = _splinePath.transform.TransformPoint((Vector3)localPos);
        Vector3 worldTan = _splinePath.transform.TransformDirection((Vector3)localTan);

        transform.position = worldPos;

        // Orient along tangent — flatten Y so fish stays upright
        Vector3 flatTan = new Vector3(worldTan.x, 0f, worldTan.z);
        if (flatTan.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flatTan, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, _turnSpeed * delta);
        }
    }
}
