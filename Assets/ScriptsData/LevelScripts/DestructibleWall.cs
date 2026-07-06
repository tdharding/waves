using System.Collections;
using UnityEngine;

/// <summary>
/// A single destructible wall piece. These are spawned in bulk along a spline
/// (see LevelSpawner.SpawnSplineWalls), so a catapult impact clears a section by
/// shattering every piece within its blast radius — CatapultProjectile calls
/// Shatter() on each DestructibleWall it finds in that radius.
///
/// Destruction is a material dissolve: the shader's "DestructableFactor" property is
/// tweened from its default up to a target value, which makes the piece disappear.
/// Once the tween finishes the object is destroyed.
///
/// The piece needs at least one Collider so the projectile's OverlapSphere finds it,
/// and its material must expose the DestructableFactor property.
/// </summary>
public class DestructibleWall : MonoBehaviour
{
    [Header("Dissolve")]
    [Tooltip("Shader property (reference name) driving the dissolve. Must match the exposed property on the wall material.")]
    public string factorProperty = "DestructableFactor";
    [Tooltip("Starting value of the property (the material's default).")]
    public float factorFrom = 1f;
    [Tooltip("Value the property is tweened to — the point at which the piece has fully disappeared.")]
    public float factorTo = 7f;
    [Tooltip("Seconds for the dissolve tween.")]
    public float dissolveDuration = 0.6f;

    [Header("FX (optional)")]
    [Tooltip("One-shot VFX spawned at the piece position when it starts to shatter. Optional.")]
    public GameObject shatterVFXPrefab;

    private bool _shattered;
    private int  _factorId;
    private Renderer[] _renderers;
    private MaterialPropertyBlock _mpb;

    public bool IsShattered => _shattered;

    /// <summary>
    /// Begin the dissolve. Safe to call more than once — only the first call acts.
    /// </summary>
    /// <param name="hitPosition">World point the blast originated from (used only for FX placement).</param>
    public void Shatter(Vector3 hitPosition)
    {
        if (_shattered) return;
        _shattered = true;

        if (shatterVFXPrefab != null)
            Instantiate(shatterVFXPrefab, transform.position, Quaternion.identity);

        _renderers = GetComponentsInChildren<Renderer>();
        if (_renderers.Length == 0)
        {
            // Nothing to dissolve — just remove it.
            Destroy(gameObject);
            return;
        }

        _factorId = Shader.PropertyToID(factorProperty);
        _mpb      = new MaterialPropertyBlock();

        StartCoroutine(DissolveRoutine());
    }

    private IEnumerator DissolveRoutine()
    {
        float duration = Mathf.Max(0.0001f, dissolveDuration);
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float value = Mathf.Lerp(factorFrom, factorTo, t);
            ApplyFactor(value);
            yield return null;
        }

        ApplyFactor(factorTo);
        Destroy(gameObject);
    }

    private void ApplyFactor(float value)
    {
        foreach (Renderer r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(_factorId, value);
            r.SetPropertyBlock(_mpb);
        }
    }
}
