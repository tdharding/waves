using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LureAttractable : MonoBehaviour
{
    // Return params come from LureBehaviour, captured in BeginReturn()

    private SplineAnimate        _splineAnimate;
    private LureBehaviour        _targetLure;
    private FishFishingBehaviour _fishingBehaviour;
    private float                _orbitAngleOffset;
    private Vector3              _returnTarget;
    private float                _returnNormalizedTime;
    private float                _returnSpeed     = 2f;
    private float                _returnThreshold = 0.3f;

    public enum State { Free, Attracted, Returning }
    public State CurrentState => _state;
    private State _state = State.Free;

    void Awake()
    {
        _splineAnimate    = GetComponent<SplineAnimate>();
        _fishingBehaviour = GetComponent<FishFishingBehaviour>();
        _orbitAngleOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        // Whirl takes full priority
        if (_fishingBehaviour != null && _fishingBehaviour.IsBeingAttracted)
        {
            if (_state == State.Attracted)
                BeginReturn();
            return;
        }

        // Find nearest active lure.
        // If already attracted, we stick to our current target as long as it's active.
        LureBehaviour nearest = null;
        float nearestDist     = float.MaxValue;

        if (_state == State.Attracted && _targetLure != null && LureBehaviour.ActiveLures.Contains(_targetLure))
        {
            nearest     = _targetLure;
            nearestDist = Vector3.Distance(transform.position, nearest.transform.position);
        }
        else
        {
            foreach (var lure in LureBehaviour.ActiveLures)
            {
                if (lure == null) continue;
                float d = Vector3.Distance(transform.position, lure.transform.position);
                if (d <= lure.attractionRadius && d < nearestDist)
                {
                    nearestDist = d;
                    nearest     = lure;
                }
            }
        }

        if (nearest != null)
        {
            if (_state != State.Attracted)
            {
                Debug.Log($"[LureAttractable] {name} ATTRACTED to {nearest.name} — dist: {nearestDist:F2}, active lures: {LureBehaviour.ActiveLures.Count}");

                if (_splineAnimate != null && _splineAnimate.IsPlaying)
                {
                    _splineAnimate.Pause();
                    Debug.Log($"[LureAttractable] {name} LEFT SPLINE at NormalizedTime={_splineAnimate.NormalizedTime:F4}, worldPos={transform.position}");
                }
            }

            _targetLure = nearest;
            _state      = State.Attracted;

            float orbitRadius     = nearest.orbitRadius;
            float orbitSpeed      = nearest.orbitSpeed;
            float moveTowardSpeed = nearest.moveTowardSpeed;

            float angle    = _orbitAngleOffset + Time.time * orbitSpeed;
            Vector3 target = _targetLure.transform.position
                           + new Vector3(Mathf.Cos(angle) * orbitRadius, 0f, Mathf.Sin(angle) * orbitRadius);

            float distToOrbit = Vector3.Distance(transform.position, target);
            float speed       = distToOrbit > orbitRadius ? moveTowardSpeed : orbitSpeed * orbitRadius;

            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

            Vector3 dir = target - transform.position;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir.normalized), Time.deltaTime * 5f);
        }
        else if (_state == State.Attracted)
        {
            BeginReturn();
        }
        else if (_state == State.Returning)
        {
            // Before heading back to spline, check if any active lure exists (no radius filter)
            LureBehaviour nearestAny = null;
            float nearestAnyDist = float.MaxValue;
            foreach (var lure in LureBehaviour.ActiveLures)
            {
                if (lure == null) continue;
                float d = Vector3.Distance(transform.position, lure.transform.position);
                if (d < nearestAnyDist) { nearestAnyDist = d; nearestAny = lure; }
            }

            if (nearestAny != null)
            {
                Debug.Log($"[LureAttractable] {name} REDIRECTED to lure {nearestAny.name} instead of returning to spline — dist: {nearestAnyDist:F2}");
                _targetLure = nearestAny;
                _state      = State.Attracted;
            }
            else
            {
            float dist = Vector3.Distance(transform.position, _returnTarget);
            transform.position = Vector3.MoveTowards(transform.position, _returnTarget, _returnSpeed * Time.deltaTime);

            Vector3 dir = _returnTarget - transform.position;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir.normalized), Time.deltaTime * 5f);

            if (dist <= _returnThreshold)
            {
                if (_splineAnimate != null)
                {
                    Vector3 splineEvalPos = _splineAnimate.Container.transform.TransformPoint(
                        (Vector3)SplineUtility.EvaluatePosition(_splineAnimate.Container.Spline, _returnNormalizedTime));

                    Debug.Log($"[LureAttractable] {name} SNAP TO SPLINE — soulPos={transform.position}, splineEvalAtT={splineEvalPos}, t={_returnNormalizedTime:F4}, snapDelta={Vector3.Distance(transform.position, splineEvalPos):F4}");

                    // Account for SplineAnimate.StartOffset which is added to NormalizedTime during evaluation
                    float adjustedTime = _returnNormalizedTime - _splineAnimate.StartOffset;
                    _splineAnimate.NormalizedTime = Mathf.Repeat(adjustedTime, 1f);

                    // Set position immediately to prevent 1-frame jump
                    transform.position = splineEvalPos;
                    _splineAnimate.Play();
                    }
                _state = State.Free;
            }
            }
        }
    }

    void BeginReturn()
    {
        if (_targetLure != null)
        {
            _returnSpeed     = _targetLure.returnSpeed;
            _returnThreshold = _targetLure.returnThreshold;
        }
        _targetLure = null;
        _state      = State.Returning;

        if (_splineAnimate != null && _splineAnimate.Container != null)
        {
            var spline   = _splineAnimate.Container.Spline;
            var localPos = (float3)_splineAnimate.Container.transform
                               .InverseTransformPoint(transform.position);

            SplineUtility.GetNearestPoint(spline, localPos,
                out float3 nearestLocal, out float t);

            _returnNormalizedTime = t;
            _returnTarget = _splineAnimate.Container.transform
                                .TransformPoint((Vector3)nearestLocal);

            Debug.Log($"[LureAttractable] {name} BEGIN RETURN — currentPos={transform.position}, nearestSplineWorldPos={_returnTarget}, t={_returnNormalizedTime:F4}, distToTarget={Vector3.Distance(transform.position, _returnTarget):F2}");
        }
        else
        {
            // Fallback — no spline info, just resume
            if (_splineAnimate != null) _splineAnimate.Play();
            _state = State.Free;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        float orbitRadius = (_targetLure != null) ? _targetLure.orbitRadius : 1.2f;
        Color discColor   = _state == State.Attracted ? new Color(1f, 0.6f, 0f, 0.8f)
                          : _state == State.Returning  ? new Color(0.4f, 1f, 0.4f, 0.8f)
                          : new Color(0.4f, 0.8f, 1f, 0.4f);

        Handles.color = discColor;
        Handles.DrawWireDisc(transform.position, Vector3.up, orbitRadius);

        if (_state == State.Attracted && _targetLure != null)
        {
            Handles.color = new Color(1f, 0.6f, 0f, 0.9f);
            Handles.DrawDottedLine(transform.position, _targetLure.transform.position, 3f);
            Handles.Label(transform.position + Vector3.up * 0.4f, "Attracted");
        }
        else if (_state == State.Returning)
        {
            Handles.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            Handles.DrawDottedLine(transform.position, _returnTarget, 3f);
            Handles.DrawWireDisc(_returnTarget, Vector3.up, 0.2f);
            Handles.Label(transform.position + Vector3.up * 0.4f, "Returning");
        }
        else
        {
            Handles.Label(transform.position + Vector3.up * 0.4f, "Free");
        }
    }
#endif
}
