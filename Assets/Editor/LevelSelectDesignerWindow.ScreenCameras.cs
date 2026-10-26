using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR

/// <summary>
/// What a rim node screen does when the key is pressed: its poster fades in on the screen itself
/// (a picture quad just in front of the glow) and, with Screen Camera on, the view cuts to a camera
/// standing in front of it. See <see cref="LevelSelectScreenInteract"/> and
/// <see cref="LevelSelectScreenCamera"/> for the runtime.
///
/// Screen Camera follows Camera Influence: switching it on spawns ScreenCamera_&lt;node&gt;_&lt;L/R&gt;
/// under SCREENCAMERAS, square in front of the screen and far enough back to frame it; Tweak in
/// Cinemachine View opens it there to set by hand. SCREENCAMERAS is never cleared — the cameras are
/// set by hand — so switching it off only switches the camera off. It is pinned to its screen: each
/// time the screen is rebuilt somewhere else the camera is moved and turned with it. It is removed
/// when the screen is.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string ScreenCamerasParent = "SCREENCAMERAS";
    private const string ScreenPictureChild  = "Picture";

    // How much of the view's height (or width, whichever is tighter) a fresh camera gives the screen.
    private const float ScreenCameraFill = 0.8f;

    private const string RimNodeScreenPictureMaterialPath =
        "Assets/TextureMatShader/LevelSelectMaterials/RimNodeScreenPictureMat.mat";

    private static string ScreenCameraName(string nodeId, int side) =>
        $"ScreenCamera_{SanitiseAssetName(nodeId)}_{(side > 0 ? "R" : "L")}";

    /// <summary>A screen's camera, switched on or not.</summary>
    private static LevelSelectScreenCamera FindScreenCamera(string nodeId, int side)
    {
        var parent = FindParent(ScreenCamerasParent);
        var t      = parent != null ? parent.transform.Find(ScreenCameraName(nodeId, side)) : null;
        return t != null ? t.GetComponent<LevelSelectScreenCamera>() : null;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>
    /// The screen's own part of the Interact block — the fade, and the Screen Camera toggle with its
    /// tweak button. Drawn into the topping copy, written back by the caller's change check.
    /// </summary>
    private void DrawScreenInteractExtras(LevelSelectDesignerData.RimNodeTopping edited, string nodeId, int side)
    {
        if (edited.interact == null || !edited.interact.enabled) return;

        EditorGUI.indentLevel++;
        edited.screenFadeTime = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Fade Time", "How long the picture takes to fade in on the screen once the " +
                                        "key is pressed, in seconds. Pressing it again takes it straight down."),
            edited.screenFadeTime));

        edited.screenCamera = EditorGUILayout.ToggleLeft(new GUIContent("Screen Camera",
            "A camera standing in front of this screen. While its picture is up the view cuts to it, and " +
            "cuts straight back when the picture is put away."),
            edited.screenCamera, EditorStyles.boldLabel);

        if (edited.screenCamera)
        {
            var camera = FindScreenCamera(nodeId, side);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                using (new EditorGUI.DisabledScope(camera == null))
                    if (GUILayout.Button(new GUIContent("Tweak in Cinemachine View",
                            "Selects this screen's camera and shows its view, with its settings, in the Cinemachine View window.")))
                    {
                        Selection.activeGameObject = camera.gameObject;
                        CinemachineViewWindow.Watch(camera.GetComponent<CinemachineCamera>());
                        GUIUtility.ExitGUI();
                    }
            }
        }
        EditorGUI.indentLevel--;
    }

    // ─────────────────────────────────────────────
    // BUILDING
    // ─────────────────────────────────────────────

    /// <summary>
    /// The one material every screen's picture shares — plain unlit, see-through, so the picture is
    /// its art and fades over the glow behind it. Made the first time it is asked for.
    /// </summary>
    private static Material RimNodeScreenPictureMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RimNodeScreenPictureMaterialPath);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] URP Unlit shader not found — rim node screen " +
                             "pictures are left without a material.");
            return null;
        }

        mat = new Material(shader) { name = "RimNodeScreenPictureMat" };
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 0f);     // alpha
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        AssetDatabase.CreateAsset(mat, RimNodeScreenPictureMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>
    /// Everything a screen carries once it is placed: the picture quad just in front of it, the
    /// point that notices the boat, the interaction, and its camera brought up to date. With
    /// Interact off none of it is left on the screen, so generating again takes it away.
    /// </summary>
    private void ApplyScreenInteract(GameObject screen, LevelSelectDesignerData.RimNodeTopping topping,
                                     string nodeId, int side, float width, float height)
    {
        var interact = topping.interact;

        // The camera first — it is there or not by the toggle, whatever Interact is.
        var camera = RefreshScreenCamera(screen.transform, topping, nodeId, side, width, height);

        if (interact == null || !interact.enabled) return;

        // Just in front of the glow, the size of the screen (the parent's scale), hidden until pressed.
        var picture = new GameObject(ScreenPictureChild);
        Undo.RegisterCreatedObjectUndo(picture, "Place Rim Node Screen");
        picture.transform.SetParent(screen.transform, false);
        picture.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        picture.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var renderer = picture.AddComponent<MeshRenderer>();
        renderer.sharedMaterial    = RimNodeScreenPictureMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.enabled           = false;

        var point  = Undo.AddComponent<LevelSelectInteractPoint>(screen);
        var action = Undo.AddComponent<LevelSelectScreenInteract>(screen);

        point.prompt = interact.prompt;
        point.radius = interact.radius;

        // The library is a real asset, so this reference survives the designer's workspace copy.
        action.library      = _data.posterLibrary;
        action.posterName   = interact.posterName;
        action.picture      = renderer;
        action.fadeTime     = topping.screenFadeTime;
        action.screenCamera = camera;

        EditorUtility.SetDirty(point);
        EditorUtility.SetDirty(action);
    }

    /// <summary>
    /// A screen's camera, just after the screen was placed: spawned when the toggle is on and it has
    /// none, moved and turned with the screen when the screen now stands somewhere else, and
    /// switched on or off with the toggle.
    /// </summary>
    private LevelSelectScreenCamera RefreshScreenCamera(Transform screen, LevelSelectDesignerData.RimNodeTopping topping,
                                                        string nodeId, int side, float width, float height)
    {
        Vector3 centre = screen.position;
        Vector3 facing = -screen.forward;   // the screen looks out into the river, its back to the wall
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-8f) facing = Vector3.forward;
        facing.Normalize();

        var camera = FindScreenCamera(nodeId, side);
        if (camera == null)
        {
            if (!topping.screenCamera) return null;
            return SpawnScreenCamera(nodeId, side, centre, facing, width, height);
        }

        // Pinned: the screen moved or turned, the camera goes with it, keeping how it was set by hand.
        Vector3    pinnedFacing = camera.screenFacing.sqrMagnitude > 1e-8f ? camera.screenFacing.normalized : facing;
        Quaternion turn         = Quaternion.FromToRotation(pinnedFacing, facing);
        bool moved  = (centre - camera.screenCentre).sqrMagnitude > 1e-10f;
        bool turned = Quaternion.Angle(turn, Quaternion.identity) > 0.01f;
        if (moved || turned)
        {
            Undo.RecordObject(camera.transform, "Move Screen Camera");
            camera.transform.SetPositionAndRotation(
                centre + turn * (camera.transform.position - camera.screenCentre),
                turn * camera.transform.rotation);

            Undo.RecordObject(camera, "Move Screen Camera");
            camera.screenCentre = centre;
            camera.screenFacing = facing;
            EditorUtility.SetDirty(camera);
        }

        if (camera.gameObject.activeSelf != topping.screenCamera)
        {
            Undo.RecordObject(camera.gameObject, "Screen Camera");
            camera.gameObject.SetActive(topping.screenCamera);
        }
        return camera;
    }

    /// <summary>
    /// A new camera for a screen: square in front of it, looking straight at its middle, far enough
    /// back that the screen fills most of the view, with the follow camera's lens and a near clip
    /// close enough not to cut into it. Kept switched off — the follow camera stands where it does.
    /// </summary>
    private LevelSelectScreenCamera SpawnScreenCamera(string nodeId, int side, Vector3 centre, Vector3 facing,
                                                      float width, float height)
    {
        var follow = Object.FindAnyObjectByType<LevelSelectCameraController>();

        var lens = LensSettings.Default;
        if (follow != null && follow.cam != null) lens = follow.cam.Lens;
        if (follow != null && follow.defaultVerticalFOV > 0f) lens.FieldOfView = follow.defaultVerticalFOV;

        float aspect  = Camera.main != null && Camera.main.aspect > 0f ? Camera.main.aspect : 16f / 9f;
        float tanHalf = Mathf.Tan(0.5f * lens.FieldOfView * Mathf.Deg2Rad);
        float back    = Mathf.Max(0.5f * height / tanHalf, 0.5f * width / (tanHalf * aspect)) / ScreenCameraFill;
        lens.NearClipPlane = Mathf.Min(lens.NearClipPlane, back * 0.25f);

        Vector3 at = centre + facing * back;

        var parent = FindOrCreateParent(ScreenCamerasParent);
        var go     = new GameObject(ScreenCameraName(nodeId, side));
        Undo.RegisterCreatedObjectUndo(go, "Screen Camera");
        go.transform.SetParent(parent.transform, false);
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-facing, Vector3.up));

        var cam = go.AddComponent<CinemachineCamera>();
        cam.Lens    = lens;
        cam.enabled = false;

        var screenCamera = go.AddComponent<LevelSelectScreenCamera>();
        screenCamera.nodeId       = nodeId;
        screenCamera.side         = side;
        screenCamera.screenCentre = centre;
        screenCamera.screenFacing = facing;
        return screenCamera;
    }

    /// <summary>
    /// Every screen camera switched on or off with its toggle, and removed once its screen is gone —
    /// its rim node taken away, that side no longer a screen. Called from MarkDirty, in the same undo
    /// step as the edit, and from Generate. Spawning and pinning happen where the screen is placed.
    /// </summary>
    private void SyncScreenCameras()
    {
        if (_data == null) return;
        var parent = FindParent(ScreenCamerasParent);
        if (parent == null) return;

        foreach (var camera in parent.GetComponentsInChildren<LevelSelectScreenCamera>(true).ToList())
        {
            var rim = _data.RimNodeAt(camera.nodeId);
            bool screenThere = rim != null && CanHoldRimNode(camera.nodeId, out _) &&
                               RimSides(rim.side).Contains(camera.side) &&
                               rim.ToppingOn(camera.side).HasScreen;
            if (!screenThere)
            {
                Undo.DestroyObjectImmediate(camera.gameObject);
                continue;
            }

            bool on = rim.ToppingOn(camera.side).screenCamera;
            if (camera.gameObject.activeSelf != on)
            {
                Undo.RecordObject(camera.gameObject, "Screen Camera");
                camera.gameObject.SetActive(on);
            }
        }
    }
}

#endif
