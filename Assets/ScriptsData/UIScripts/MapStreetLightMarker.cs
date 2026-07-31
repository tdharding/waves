using UnityEngine;

/// <summary>
/// Runtime state for a procedurally-generated street-light map icon. The icon is one mesh
/// (halo + stick + bulb) built by UIMapController.BuildStreetLightIcon. This component flips it
/// between OFF and ON by rewriting only the affected vertex colours — no mesh regeneration:
///   • bulb: black (off) → white (on)   — via vertex rgb
///   • halo: hidden (off) → half-opacity (on) — via vertex alpha
/// </summary>
public class MapStreetLightMarker : MonoBehaviour
{
    private Mesh    _mesh;
    private Color[] _colors;
    private int     _bulbStart, _bulbCount, _haloStart, _haloCount;
    private float   _haloAlpha = 0.5f;
    private bool?   _lit;   // null so the first SetLit always applies

    public void Init(Mesh mesh, Color[] colors, int bulbStart, int bulbCount, int haloStart, int haloCount, float haloAlpha)
    {
        _mesh      = mesh;
        _colors    = colors;
        _bulbStart = bulbStart;  _bulbCount = bulbCount;
        _haloStart = haloStart;  _haloCount = haloCount;
        _haloAlpha = haloAlpha;
        _lit       = null;
    }

    public void SetLit(bool lit)
    {
        if (_mesh == null || _colors == null || _lit == lit) return;
        _lit = lit;

        float bulb = lit ? 1f : 0f;   // white when lit, black when off (alpha stays 1)
        for (int i = _bulbStart; i < _bulbStart + _bulbCount && i < _colors.Length; i++)
            _colors[i] = new Color(bulb, bulb, bulb, 1f);

        float halo = lit ? _haloAlpha : 0f;
        for (int i = _haloStart; i < _haloStart + _haloCount && i < _colors.Length; i++)
            _colors[i] = new Color(1f, 1f, 1f, halo);

        _mesh.colors = _colors;
    }
}
