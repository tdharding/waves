using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LureAttractable : MonoBehaviour
{
    [Header("Return")]
    public float returnSpeed     = 2f;
    public float returnThreshold = 0.3f;

    private SplineAnimate        _splineAnimate;
    private LureBehaviour        _targetLure;
    private FishFishingBehaviour _fishingBehaviour;
    private float                _orbitAngleOffset;
    private Vector3              _returnTarget;
    private float                _returnNormalizedTime;

    enum State { Free, Attracted, Returning }
    State _state = State.Free;

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

        // Find nearest active lure in range
        LureBehaviour nearest = null;
        float nearestDist     = float.MaxValue;
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

        if (nearest != null)
        {
            if (_splineAnimate != null && _splineAnimate.IsPlaying)
                _splineAnimate.Pause();

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
            float dist = Vector3.Distance(transform.position, _returnTarget);
            transform.position = Vector3.MoveTowards(transform.position, _returnTarget, returnSpeed * Time.deltaTime);

            Vector3 dir = _returnTarget - transform.position;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir.normalized), Time.deltaTime * 5f);

            if (dist <= returnThreshold)
            {
                if (_splineAnimate != null)
                {
                    _splineAnimate.NormalizedTime = _returnNormalizedTime;
                    _splineAnimate.Play();
                }
                _state = State.Free;
            }
        }
    }

    void BeginReturn()
    {
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
