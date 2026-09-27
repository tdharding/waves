using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// The Spike Studio's stage: an empty scene of its own, opened in the Scene view the way Prefab Mode
// opens a prefab. Just the rock, a breadcrumb back to your scene, and nothing else.
//
// The rock still wears the look of where it will stand — a level's rock material (Waves) or the
// map's landscape spike material (Level Select). Every look setting (Aesthetics, the tuners, grain,
// grooves) is a shared shader global, so it carries in by itself. What does NOT carry in is
// anything measured from where things stand in the world, because none of it is in this scene:
//
//   the boat      — the rock's boat light and the map's white fade are measured from it. A stand-in
//                   boat sits on the rock's waterline, on the side the camera first looks from.
//   the water     — (Waves) the waterline black band is measured from the water's height, so the
//                   water is put at the rock's own waterline.
//   placed lights — (Waves) street lights and the like light rocks near them; there are none here.
//
// Those are swapped in only while the Scene view draws this stage and put back straight after, so
// the Game view and your own scene never see them.
public class SpikeStage : PreviewSceneStage
{
    public enum Look { Waves, LevelSelect }

    // How far off the rock's waterline the stand-in boat sits, in metres — a boat passing close by.
    const float BoatStandOff = 1f;

    // The first view: a little above the waterline, looking slightly down. The boat sits on the
    // camera's side of the rock, so the face you see first is the lit one.
    static readonly Quaternion FirstView = Quaternion.Euler(12f, 0f, 0f);

    const string LevelSelectMaterialPath =
        "Assets/TextureMatShader/LevelSelectMaterials/LevelSelectSpikeMat.mat";

    static readonly int BoatCentreId      = Shader.PropertyToID("_BoatWorldCenter");
    static readonly int WhiteFadeCentreId = Shader.PropertyToID("_WhiteFadeCentre");
    static readonly int SurfaceOriginId   = Shader.PropertyToID("_WaterSurfaceOrigin");
    static readonly int LightCountId      = Shader.PropertyToID("_InstLightCount");

    [SerializeField] Look       _look;
    [SerializeField] Vector3    _standAt;
    [SerializeField] Material   _levelSelectMaterial;
    [SerializeField] GameObject _rock;
    [SerializeField] float      _waterlineRadius;

    // What the stage camera found in the globals before swapping its stand-ins in.
    bool    _swapped;
    Vector4 _savedBoatCentre, _savedWhiteFadeCentre, _savedSurfaceOrigin;
    float   _savedLightCount;

    /// <summary>The spike stage open in the Scene view, or null when it isn't.</summary>
    public static SpikeStage Current => StageUtility.GetCurrentStage() as SpikeStage;

    /// <summary>The rock standing in the stage. Null until the stage has opened.</summary>
    public GameObject Rock => _rock;

    public Look CurrentLook => _look;

    /// <summary>
    /// Opens the stage — or re-dresses the one already open — with the rock standing at
    /// <paramref name="standAt"/> in the given look. <paramref name="levelSelectMaterial"/> is the
    /// Level Select Designer's own spike material; null falls back to LevelSelectSpikeMat.
    /// </summary>
    public static SpikeStage Open(Look look, Vector3 standAt, Material levelSelectMaterial)
    {
        var stage = Current;
        if (stage != null)
        {
            stage.Dress(look, standAt, levelSelectMaterial);
            return stage;
        }

        stage = CreateInstance<SpikeStage>();
        stage._look                = look;
        stage._standAt             = standAt;
        stage._levelSelectMaterial = levelSelectMaterial;
        StageUtility.GoToStage(stage, true);

        // Going to a stage can be refused (Prefab Mode asking to save, say), in which case the
        // stage never opened and there is nothing to hand back.
        return Current == stage ? stage : null;
    }

    /// <summary>Back to the scene you came from.</summary>
    public static void Close()
    {
        if (Current != null) StageUtility.GoToMainStage();
    }

    /// <summary>Changes the look and where the rock stands, without reopening.</summary>
    public void Dress(Look look, Vector3 standAt, Material levelSelectMaterial)
    {
        _look                = look;
        _standAt             = standAt;
        _levelSelectMaterial = levelSelectMaterial;
        DressRock();
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Swaps the rock's mesh for a freshly built one (the old one is destroyed).
    /// <paramref name="waterlineRadius"/> is the rock's radius where it meets the water, which the
    /// stand-in boat keeps its distance from.
    /// </summary>
    public void SetMesh(Mesh mesh, float waterlineRadius)
    {
        if (_rock == null) return;
        _waterlineRadius = Mathf.Max(0f, waterlineRadius);
        var filter = _rock.GetComponent<MeshFilter>();
        var old    = filter.sharedMesh;
        filter.sharedMesh = mesh;
        if (old != null && old != mesh) DestroyImmediate(old);
        SceneView.RepaintAll();
    }

    /// <summary>Moves an object into this stage's scene, so it shows here and never in your scene.</summary>
    public void Adopt(GameObject go)
    {
        if (go != null && go.scene != scene) EditorSceneManager.MoveGameObjectToScene(go, scene);
    }

    /// <summary>Frames the part of the rock that stands proud, in every Scene view.</summary>
    public void Frame()
    {
        foreach (SceneView sv in SceneView.sceneViews) Frame(sv);
    }

    void Frame(SceneView sv)
    {
        if (sv == null || _rock == null) return;
        var mesh = _rock.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) return;

        // The mesh is built around its waterline at y = 0, and what you design is what stands
        // above it — most of a rock is buried, so framing its whole bounds would frame the footing.
        Bounds b      = mesh.bounds;
        float  proud  = Mathf.Max(b.max.y, 0.1f);
        float  across = Mathf.Max(b.extents.x, b.extents.z) * 2f;
        Vector3 pivot = _standAt + Vector3.up * proud * 0.5f;
        sv.LookAt(pivot, FirstView, Mathf.Max(proud, across) * 0.75f, false, true);
    }

    // ─────────────────────────────────────────────
    // STAGE
    // ─────────────────────────────────────────────

    protected override GUIContent CreateHeaderContent() => new GUIContent("Spike Studio");

    protected override bool OnOpenStage()
    {
        if (!base.OnOpenStage()) return false;

        _rock = new GameObject("Spike", typeof(MeshFilter), typeof(MeshRenderer));
        Adopt(_rock);
        DressRock();
        return true;
    }

    protected override void OnCloseStage()
    {
        if (_rock != null)
        {
            var mesh = _rock.GetComponent<MeshFilter>().sharedMesh;
            if (mesh != null) DestroyImmediate(mesh);
        }
        _rock = null;
        base.OnCloseStage();
    }

    protected override void OnFirstTimeOpenStageInSceneView(SceneView sceneView) => Frame(sceneView);

    protected override void OnEnable()
    {
        base.OnEnable();
        RenderPipelineManager.beginCameraRendering += SwapStandInsIn;
        RenderPipelineManager.endCameraRendering   += PutRealValuesBack;
    }

    protected override void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= SwapStandInsIn;
        RenderPipelineManager.endCameraRendering   -= PutRealValuesBack;
        if (_swapped) Restore();
        base.OnDisable();
    }

    void DressRock()
    {
        if (_rock == null) return;
        _rock.transform.SetPositionAndRotation(_standAt, Quaternion.identity);
        _rock.transform.localScale = Vector3.one;
        _rock.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(_look);
    }

    Material MaterialFor(Look look)
    {
        if (look == Look.LevelSelect)
            return _levelSelectMaterial != null
                ? _levelSelectMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(LevelSelectMaterialPath);

        // The maze rock material, so the rock wears what a level's spikes wear.
        foreach (var guid in AssetDatabase.FindAssets("MazeSpikeOpaque t:Material"))
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var guid in AssetDatabase.FindAssets("Spikesmat t:Material"))
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
        return null;
    }

    // ─────────────────────────────────────────────
    // STAND-INS
    // ─────────────────────────────────────────────

    /// <summary>
    /// The stand-in boat: on the rock's waterline, just off its edge, on the side the first view
    /// looks from (the first view faces +Z, so that side is −Z).
    /// </summary>
    Vector3 BoatAt() => _standAt + Vector3.back * (_waterlineRadius + BoatStandOff);

    void SwapStandInsIn(ScriptableRenderContext context, Camera cam)
    {
        if (_swapped || _rock == null) return;
        if (cam.cameraType != CameraType.SceneView || StageUtility.GetCurrentStage() != this) return;

        _savedBoatCentre      = Shader.GetGlobalVector(BoatCentreId);
        _savedWhiteFadeCentre = Shader.GetGlobalVector(WhiteFadeCentreId);
        _savedSurfaceOrigin   = Shader.GetGlobalVector(SurfaceOriginId);
        _savedLightCount      = Shader.GetGlobalFloat(LightCountId);
        _swapped = true;

        Vector3 boat = BoatAt();

        if (_look == Look.Waves)
        {
            Shader.SetGlobalVector(BoatCentreId, boat);

            // The water at the rock's own waterline. Only the height: the rings keep their centre,
            // so the band still rides the wave the way it does in a level.
            Vector4 origin = _savedSurfaceOrigin;
            origin.y = _standAt.y;
            Shader.SetGlobalVector(SurfaceOriginId, origin);

            Shader.SetGlobalFloat(LightCountId, 0f);
        }
        else
        {
            // The same pair the map's boat pusher (LevelSelectBoatToShaders) writes.
            Shader.SetGlobalVector(BoatCentreId,      boat);
            Shader.SetGlobalVector(WhiteFadeCentreId, boat);
        }
    }

    void PutRealValuesBack(ScriptableRenderContext context, Camera cam)
    {
        if (_swapped) Restore();
    }

    void Restore()
    {
        Shader.SetGlobalVector(BoatCentreId,      _savedBoatCentre);
        Shader.SetGlobalVector(WhiteFadeCentreId, _savedWhiteFadeCentre);
        Shader.SetGlobalVector(SurfaceOriginId,   _savedSurfaceOrigin);
        Shader.SetGlobalFloat(LightCountId,       _savedLightCount);
        _swapped = false;
    }
}
