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

    /// <summary>
    /// Debug read-only: what this icon currently shows. Null means SetLit has never run on it, so
    /// the icon is still in whatever state it was built in.
    /// </summary>
    public bool? DebugLitState => _lit;

    /// <summary>
    /// Debug read-only: the colours SetLit writes and the colours actually sitting on the mesh.
    /// If these two agree and the icon still looks unlit, the write landed and the material is
    /// ignoring vertex colours; if the vertex ranges are empty, SetLit had nothing to write to.
    /// </summary>
    public string DebugMeshState()
    {
        if (_mesh == null || _colors == null) return "Init never ran — no mesh or colour array";

        Color[] onMesh = _mesh.colors;
        return $"lit={(_lit.HasValue ? _lit.Value.ToString() : "never set")} " +
               $"verts={_colors.Length} (mesh holds {onMesh.Length}) " +
               $"bulb[{_bulbStart}+{_bulbCount}] wants={Fmt(Sample(_colors, _bulbStart, _bulbCount))} " +
               $"onMesh={Fmt(Sample(onMesh, _bulbStart, _bulbCount))} " +
               $"halo[{_haloStart}+{_haloCount}] wants={Fmt(Sample(_colors, _haloStart, _haloCount))} " +
               $"onMesh={Fmt(Sample(onMesh, _haloStart, _haloCount))} haloAlpha={_haloAlpha:F2}";
    }

    static Color? Sample(Color[] cols, int start, int count) =>
        cols != null && count > 0 && start >= 0 && start < cols.Length ? cols[start] : (Color?)null;

    static string Fmt(Color? c) =>
        c.HasValue ? $"({c.Value.r:F2},{c.Value.g:F2},{c.Value.b:F2},a{c.Value.a:F2})" : "EMPTY RANGE";

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
