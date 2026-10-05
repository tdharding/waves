using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Model Texturing Tool's stage: an empty scene of its own, opened in the Scene view the way Prefab Mode
// opens a prefab (and the way Spike Studio opens its rock). It holds a copy of the model being
// painted — the mesh with its bones, stood in the armature's rest pose (the T-pose it was modelled
// in) — and nothing else, with a breadcrumb back to your scene.
//
// The copy wears the same material assets as the original, so a bake shows on both. Scripts are
// stripped from the copy so nothing on it runs or registers itself (cameras, companions) here.
public class ModelTexturingStage : PreviewSceneStage
{
    [SerializeField] GameObject _source;
    [SerializeField] GameObject _copy;

    /// <summary>The stage open in the Scene view, or null when it isn't.</summary>
    public static ModelTexturingStage Current => StageUtility.GetCurrentStage() as ModelTexturingStage;

    /// <summary>Raised as the stage closes, while the copy still exists.</summary>
    public static event System.Action Closed;

    /// <summary>The copy of the model standing in the stage.</summary>
    public GameObject Copy => _copy;

    /// <summary>
    /// Opens the stage — or swaps the model in the one already open — with a copy of
    /// <paramref name="modelRoot"/>. Null if the stage could not open (Prefab Mode asking to save, say).
    /// </summary>
    public static ModelTexturingStage Open(GameObject modelRoot)
    {
        var stage = Current;
        if (stage != null)
        {
            stage.Load(modelRoot);
            stage.Frame();
            return stage;
        }

        stage = CreateInstance<ModelTexturingStage>();
        stage._source = modelRoot;
        StageUtility.GoToStage(stage, true);
        return Current == stage ? stage : null;
    }

    /// <summary>Back to the scene you came from.</summary>
    public static void Close()
    {
        if (Current != null) StageUtility.GoToMainStage();
    }

    void Load(GameObject source)
    {
        if (_copy != null) DestroyImmediate(_copy);
        _source = source;
        if (source == null) return;

        _copy = Instantiate(source);
        _copy.name = source.name;
        EditorSceneManager.MoveGameObjectToScene(_copy, scene);
        _copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _copy.transform.localScale = source.transform.lossyScale;

        // Scripts can depend on each other, so a component another one requires only goes once
        // that one has gone — a few passes clears any chain.
        for (int pass = 0; pass < 4; pass++)
            foreach (var mb in _copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) DestroyImmediate(mb);

        foreach (var animator in _copy.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        PoseToBindPose(_copy);
    }

    // The skeleton back in the pose the mesh was bound in — the armature's rest pose. Each bindpose
    // is the inverse of its bone's matrix at bind time, relative to the mesh, so the bone sits at
    // mesh × bindpose⁻¹. Parents are posed before children, so each child lands where it should.
    static void PoseToBindPose(GameObject root)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh  = smr.sharedMesh;
            var bones = smr.bones;
            if (mesh == null || bones == null) continue;
            var bind = mesh.bindposes;
            if (bind.Length != bones.Length) continue;

            Matrix4x4 meshToWorld = smr.transform.localToWorldMatrix;
            foreach (int i in Enumerable.Range(0, bones.Length).Where(i => bones[i] != null).OrderBy(i => Depth(bones[i])))
            {
                Transform bone  = bones[i];
                Matrix4x4 world = meshToWorld * bind[i].inverse;
                Matrix4x4 local = bone.parent != null ? bone.parent.worldToLocalMatrix * world : world;
                bone.localPosition = local.GetColumn(3);
                bone.localRotation = local.rotation;
                bone.localScale    = local.lossyScale;
            }
        }
    }

    static int Depth(Transform t)
    {
        int d = 0;
        for (; t.parent != null; t = t.parent) d++;
        return d;
    }

    void Frame()
    {
        foreach (SceneView sv in SceneView.sceneViews) Frame(sv);
    }

    void Frame(SceneView sv)
    {
        if (sv == null || _copy == null) return;
        var renderers = _copy.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        sv.Frame(b, false);
    }

    // ─────────────────────────────────────────────
    // STAGE
    // ─────────────────────────────────────────────

    protected override GUIContent CreateHeaderContent() => new GUIContent("Model Texturing Tool");

    protected override bool OnOpenStage()
    {
        if (!base.OnOpenStage()) return false;
        Load(_source);
        return true;
    }

    protected override void OnCloseStage()
    {
        Closed?.Invoke();
        _copy = null;
        base.OnCloseStage();
    }

    protected override void OnFirstTimeOpenStageInSceneView(SceneView sceneView) => Frame(sceneView);
}
