using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;
using System.Collections;
using Unity.Mathematics;

public class SplineRiverJunctionNode : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LevelSelectBoatControl boatControl;

    [Header("Outlets")]
    // outlets[0] = LEFT arrow
    // outlets[1] = UP arrow
    // outlets[2] = RIGHT arrow
    // outlets[3] = DOWN arrow
    [SerializeField] private SplineContainerRef[] outlets;

    [Header("Transition")]
    [SerializeField] private Transform boatTransformPoint;
    [SerializeField] private float transitionSpeed = 2f;

    [Header("Junction UI")]
    [SerializeField] private GameObject junctionUIRoot;

    private LevelSelectBoatControl _boat;
    private bool _waitingForChoice;
    private bool _junctionActive;

    private void OnTriggerEnter(Collider other)
    {
        if (_junctionActive) return;
        if (!other.CompareTag("BoatPrefab")) return;

        _junctionActive = true;
        _boat = boatControl;
        _boat.HandOffToJunction();

        StartCoroutine(JunctionSequence());
    }

    private IEnumerator JunctionSequence()
    {
        if (boatTransformPoint != null)
            yield return StartCoroutine(MoveToWorldPoint(
                boatTransformPoint.position,
                boatTransformPoint.rotation,
                rotateOnArrival: false
            ));

        ShowJunctionUI();
        _waitingForChoice = true;

while (_waitingForChoice)
{
    // Check for Up Arrow -> Top Path
    if (Input.GetKeyDown(KeyCode.UpArrow))
    {
        int topIndex = FindPathIndex(true); // true for Top
        if (topIndex != -1) ChoosePath(topIndex);
    }

    // Check for Down Arrow -> Bottom Path
    if (Input.GetKeyDown(KeyCode.DownArrow))
    {
        int bottomIndex = FindPathIndex(false); // false for Bottom
        if (bottomIndex != -1) ChoosePath(bottomIndex);
    }

    // Keep your existing arrow logic for indices if you still need Left/Right
    if (Input.GetKeyDown(KeyCode.LeftArrow)  && outlets.Length > 0) ChoosePath(0);
    if (Input.GetKeyDown(KeyCode.RightArrow) && outlets.Length > 2) ChoosePath(2);

    yield return null;
}
    }

    private int FindPathIndex(bool lookForTop)
{
    for (int i = 0; i < outlets.Length; i++)
    {
        if (lookForTop && outlets[i].isLeftPath)
        {
            return i;
        }
        if (!lookForTop && outlets[i].isRightPath)
        {
            return i;
        }
    }
    return -1; // No matching path found
}

    private void ShowJunctionUI()
    {
        if (junctionUIRoot != null) junctionUIRoot.SetActive(true);
    }

    private void HideJunctionUI()
    {
        if (junctionUIRoot != null) junctionUIRoot.SetActive(false);
    }

    private void ChoosePath(int index)
    {
        if (index < 0 || index >= outlets.Length) return;

        _waitingForChoice = false;
        _junctionActive = false;

        HideJunctionUI();

        StartCoroutine(TransitionToSpline(outlets[index]));
    }

    private IEnumerator TransitionToSpline(SplineContainerRef outlet)
    {
        float entryT = outlet.entryFromStart ? 0f : 1f;
        Vector3 entryWorldPos = GetSplineWorldPosition(outlet.container, entryT);
        Vector3 entryTangent  = GetSplineWorldTangent(outlet.container, entryT, outlet.entryFromStart);

        Vector3 travelDirection = outlet.entryFromStart ? entryTangent : -entryTangent;

        if (outlet.isLeftPath || outlet.isRightPath)
        {
            float meshY = _boat.MeshTransform.localEulerAngles.y;
            Debug.Log(meshY);
            if (meshY >= 170f && meshY <= 190f) travelDirection = -travelDirection;
        }

        Quaternion finalRot = Quaternion.LookRotation(travelDirection, Vector3.up);

        yield return StartCoroutine(MoveToWorldPoint(entryWorldPos, finalRot));

        _boat.AttachToSegment(outlet.container, entryT, outlet.isLeftPath, outlet.isRightPath);
    }

    private IEnumerator MoveToWorldPoint(Vector3 targetPos, Quaternion targetRot, bool rotateOnArrival = true)
    {
        Vector3    startPos = _boat.BoatTransform.position;
        Quaternion startRot = _boat.BoatTransform.rotation;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * transitionSpeed;
            float smooth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            _boat.BoatTransform.position = Vector3.Lerp(startPos, targetPos, smooth);

            if (rotateOnArrival)
                _boat.BoatTransform.rotation = Quaternion.Slerp(startRot, targetRot, smooth);

            yield return null;
        }

        _boat.BoatTransform.position = targetPos;
        _boat.BoatTransform.rotation = rotateOnArrival ? targetRot : _boat.BoatTransform.rotation;
    }

    private Vector3 GetSplineWorldPosition(SplineContainer container, float t)
    {
        float3 localPos = container.Spline.EvaluatePosition(t);
        return container.transform.TransformPoint(localPos);
    }

    private Vector3 GetSplineWorldTangent(SplineContainer container, float t, bool entryFromStart)
    {
        float3 localTangent = container.Spline.EvaluateTangent(t);
        Vector3 worldTangent = container.transform.TransformDirection(localTangent);

        if (!entryFromStart) worldTangent = -worldTangent;

        return worldTangent.normalized;
    }
}

[System.Serializable]
public class SplineContainerRef
{
    public SplineContainer container;
    public bool entryFromStart = true;
    [FormerlySerializedAs("isLeftPath")]    public bool isLeftPath  = false;
    [FormerlySerializedAs("isRightPath")] public bool isRightPath = false;
}