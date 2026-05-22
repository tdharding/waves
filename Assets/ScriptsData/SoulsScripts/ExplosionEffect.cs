using UnityEngine;

public class ExplosionEffect : MonoBehaviour
{
    [Header("Lifetime")]
    public float lifetime = 1.0f;

    [Header("Scale")]
    public float startScale = 0.3f;
    public float endScale   = 1.0f;

    private MeshRenderer        _renderer;
    private MaterialPropertyBlock _mpb;
    private float               _elapsed;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _mpb      = new MaterialPropertyBlock();
        transform.localScale = Vector3.one * startScale;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / lifetime);

        // Scale: 0.3 → 1 with smooth ease
        transform.localScale = Vector3.one * Mathf.SmoothStep(startScale, endScale, t);

        // Alpha: 0 → 1 → 0 (peaks at midpoint)
        float alpha = 1f - Mathf.Abs(t * 2f - 1f);

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat("_Alpha", alpha);
        _renderer.SetPropertyBlock(_mpb);

        if (_elapsed >= lifetime)
            Destroy(gameObject);
    }
}
