using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class ShopItemDisplay : MonoBehaviour
{
    [Header("Positioning")]
    [Tooltip("How far above the spawn point the item floats.")]
    public float verticalOffset = 0.5f;

    [Header("Bob")]
    [Tooltip("Peak distance above and below the resting position.")]
    public float bobAmplitude = 0.15f;
    [Tooltip("Full cycles per second.")]
    public float bobSpeed = 1.2f;
    [Tooltip("Phase offset in seconds — stagger multiple items so they don't bob in sync.")]
    public float bobPhase = 0f;

    [Header("Spin")]
    [Tooltip("Degrees per second around the world-up axis.")]
    public float spinSpeed = 60f;

    [Header("Click")]
    public UnityEvent onClick;

    private Vector3 _baseLocalPosition;

    private void Start()
    {
        _baseLocalPosition      = transform.localPosition + Vector3.up * verticalOffset;
        transform.localPosition = _baseLocalPosition;
    }

    private void Update()
    {
        float bob = Mathf.Sin((Time.time + bobPhase) * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
        transform.localPosition = _baseLocalPosition + Vector3.up * bob;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    private void OnMouseDown()
    {
        onClick.Invoke();
    }
}
