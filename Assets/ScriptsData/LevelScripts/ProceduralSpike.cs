using UnityEngine;

// Generates the mesh for a procedural spike at spawn time, and — when the Grid Designer flagged
// the rock climbable — fits the creepy guy's climbing rings to the very same silhouette.
//
// The Grid Designer stores each spike's shape on GridData.ProceduralSpike; LevelSpawner
// instantiates this prefab and calls Build() with the world-space profile. The mesh is built
// around the prefab origin with the waterline at y = 0 (matching ProceduralCubeBuilding, whose
// origin LevelSpawner aligns to the water plane), so the rock meets the water where it should.
//
// The same mesh goes to the visible child and the warning-line overlay, plus the visible
// child's MeshCollider — the arrangement ProceduralCubeBuilding already uses.
[DisallowMultipleComponent]
public class ProceduralSpike : MonoBehaviour
{
    [Header("Child object names (auto-resolved by name if unassigned)")]
    [SerializeField] string visibleChildName = "MeshVisible";
    [SerializeField] string warningLinesChildName = "WarningCollisionLines";

    [Header("Optional explicit references (override name lookup)")]
    [SerializeField] GameObject visibleChild;
    [SerializeField] GameObject warningLinesChild;

    [Header("Climbing")]
    [Tooltip("How far beneath the waterline the creepy guy surfaces on a climbable spike. " +
             "Deep enough that he rises out of the water rather than fading in on top of it.")]
    [SerializeField] float surfacingDepth = 0.25f;

    [Tooltip("How far below the tip his top ring sits, as a fraction of the height above water. " +
             "Keeps him off the very point of a spike that tapers to a needle.")]
    [Range(0f, 0.5f)] [SerializeField] float topRingDrop = 0.08f;

    [Tooltip("Smallest ring he will be given. A spike that comes to a point would otherwise " +
             "leave him spinning on the spot with nowhere to walk.")]
    [SerializeField] float minRingRadius = 0.04f;

    [Header("Editor preview shape")]
    [Tooltip("Shape used by the Rebuild Preview context menu so the prefab isn't empty in the " +
             "project view. Levels ignore this — they pass the placement's own preset.")]
    [SerializeField] SpikeShapeConfig previewShape = new SpikeShapeConfig();

    /// <summary>
    /// Builds the rock from a shape preset at the given size and, when climbable, fits the creepy
    /// guy to it. Not climbable strips the CreepClimbingArea off the instance entirely, so the
    /// rock never joins his route and stays scenery.
    /// </summary>
    public void Build(SpikeShapeConfig cfg, float scale, bool climbable,
                      bool angelPerchPoint = false, float angelPerchRadius = 12f,
                      float angelTalkRadius = 4f, bool angelPriorityPerch = false,
                      bool angelTalkEnabled = false, string angelTalkText = "",
                      float angelLandingCurveSize = 2f)
    {
        if (cfg == null) cfg = new SpikeShapeConfig();

        var  profile = SpikeProfile.From(cfg, scale);
        Mesh mesh    = ProceduralSpikeMesh.Build(profile, cfg.sidesAround, cfg.heightSubdivisions,
                                                 SpikeRidge.From(cfg, scale), cfg.twistTurns);
        mesh.name = "ProceduralSpike";

        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);

        FitClimbingArea(profile, climbable);
        FitAngelPerch(profile, angelPerchPoint, angelPerchRadius, angelTalkRadius, angelPriorityPerch,
                      angelTalkEnabled, angelTalkText, angelLandingCurveSize);
        FitRockRings(profile, cfg, scale);
    }

    // The angel lands on the point of the rock, so the perch is simply the tip the mesh was just
    // built to — straight up this object's own axis, since the mesh is built around the origin.
    // Added on demand rather than expected on the prefab (the same route RockRingSource takes),
    // so marking a rock in the Grid Designer is all it takes.
    void FitAngelPerch(SpikeProfile profile, bool angelPerchPoint,
                       float perchRadius, float talkRadius, bool priority,
                       bool talkEnabled, string talkText, float landingCurveSize)
    {
        var perch = GetComponent<AngelPerchPoint>();

        if (!angelPerchPoint)
        {
            // Gone rather than disabled: AngelPerchPoint registers itself in OnEnable, and a
            // disabled one left behind would still be found by anything walking children.
            if (perch != null) Destroy(perch);
            return;
        }

        if (perch == null) perch = gameObject.AddComponent<AngelPerchPoint>();
        perch.Configure(new Vector3(0f, profile.topY, 0f), perchRadius, talkRadius, priority,
                        talkEnabled, talkText, landingCurveSize);
    }

    // The bands the water throws around this rock are drawn from the shape just built, so the
    // ring on the surface belongs to the rock standing in it rather than being a circle that
    // happens to sit nearby. A rock with no carved ridges genuinely IS a circle at the waterline,
    // so it reports no lobes and throws plain circular bands.
    void FitRockRings(SpikeProfile profile, SpikeShapeConfig cfg, float scale)
    {
        var source = GetComponent<RockRingSource>();
        if (source == null) source = gameObject.AddComponent<RockRingSource>();

        float waterline = Mathf.Max(profile.RadiusAt(0f), 0.0001f);
        var   ridge     = SpikeRidge.From(cfg, scale);
        float s         = scale > 0.0001f ? scale : 1f;

        // How far the flutes cut, as a fraction of the rock's girth here — its real cross-section.
        float amplitude = ridge.enabled ? Mathf.Clamp01(ridge.depth / waterline) : 0f;

        // The lobes carry on winding as the bands spread, at the rock's own pitch: its twist over
        // its height above water, expressed in radians per world unit travelled outward.
        float height = cfg.heightAboveWater * s;
        float twist  = height > 0.0001f ? cfg.twistTurns * Mathf.PI * 2f / height : 0f;

        source.Configure(waterline, ridge.enabled ? ridge.count : 0f, amplitude, twist);
    }

    // The three rings, read straight off the shape the designer drew:
    //   spawn — just under the waterline, where he surfaces
    //   lower — the mid ring, the belly or waist of the rock
    //   upper — just under the tip, where he leaps from
    void FitClimbingArea(SpikeProfile profile, bool climbable)
    {
        var area = GetComponentInChildren<CreepClimbingArea>(true);
        if (area == null)
        {
            if (climbable)
                Debug.LogWarning($"[ProceduralSpike] '{name}' is flagged climbable but the prefab has no " +
                                 "CreepClimbingArea — the creepy guy will ignore it.", this);
            return;
        }

        if (!climbable)
        {
            // Gone rather than disabled: CreepClimbingArea registers itself in OnEnable, and a
            // disabled one left on the prefab would still be found by anything walking children.
            Destroy(area);
            return;
        }

        float spawnY = -Mathf.Abs(surfacingDepth);
        float upperY = Mathf.Lerp(profile.topY, profile.midY, topRingDrop);

        // The area's rings live in ITS local space; the profile is in the spike root's space.
        // They are the same space unless the prefab nests the area under an offset child.
        area.Configure(area.transform.InverseTransformPoint(transform.position),
                       Mathf.Max(minRingRadius, profile.RadiusAt(spawnY)), spawnY,
                       Mathf.Max(minRingRadius, profile.RadiusAt(profile.midY)), profile.midY,
                       Mathf.Max(minRingRadius, profile.RadiusAt(upperY)), upperY);
    }

#if UNITY_EDITOR
    // Lets the prefab be previewed with a mesh in the editor without entering play mode.
    [ContextMenu("Rebuild Preview")]
    void RebuildPreview()
    {
        var  cfg  = previewShape ?? new SpikeShapeConfig();
        Mesh mesh = ProceduralSpikeMesh.Build(SpikeProfile.From(cfg), cfg.sidesAround,
                                              cfg.heightSubdivisions, SpikeRidge.From(cfg), cfg.twistTurns);
        mesh.name = "ProceduralSpikePreview";
        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);
    }
#endif

    GameObject ResolveChild(GameObject explicitRef, string childName)
    {
        if (explicitRef != null) return explicitRef;
        if (string.IsNullOrEmpty(childName)) return null;
        Transform t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    static void ApplyMesh(GameObject go, Mesh mesh, bool assignCollider)
    {
        if (go == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        if (assignCollider)
        {
            var col = go.GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = mesh;
        }
    }
}
