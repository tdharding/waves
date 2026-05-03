using UnityEngine;

public class SplineSplitterPreset : ScriptableObject
{
    public int        subdivisions          = 20;
    public float      splitT                = 0.5f;
    public bool       keepOriginal          = false;
    public bool       copySplineInstantiate = true;
    public bool       splitToSameObject     = false;
    public int        knotMode              = 0;   // KnotTangentMode as int
    public bool       addJunction           = false;
    public GameObject junctionPrefab;               // fallback visual (gap SplineInstantiate)
    public GameObject junctionRightFacingPrefab;     // visual: branch exits right of travel direction
    public GameObject junctionLeftFacingPrefab;       // visual: branch exits left  of travel direction
    public GameObject junctionScriptObject;         // LevelSelectJunctionScriptObject — placed under RIVERJUNCTIONS
    public float      padding               = 0f;
    public Vector3    junctionPosOffset;
    public Vector3    junctionRotOffset;
}
