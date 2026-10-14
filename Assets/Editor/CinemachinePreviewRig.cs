using System;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Renders what a Cinemachine camera sees into a texture, without touching the game's own camera: a
// hidden throwaway camera is parked on the vcam's state and rendered on demand. Shared by the
// Cinemachine View window and the Camera Sequence storyboard's shot previews.
public sealed class CinemachinePreviewRig : IDisposable
{
    const int MaxTargetSize = 4096;

    Camera previewCam;

    // The renderer index lives in a serialized field with no public getter, so it costs a
    // SerializedObject to read. Cached against the camera it was read from.
    int cachedRendererIndex = -1;
    UniversalAdditionalCameraData cachedRendererSource;

    // Renders the camera's view into the target. False when there is nothing to render with.
    // beforeRender is handed the hidden camera once it is standing where the view is, just before it draws.
    public bool Render(CinemachineVirtualCameraBase cam, RenderTexture target, Action<Camera> beforeRender = null)
    {
        if (cam == null || target == null) return false;

        EnsureRig();
        if (previewCam == null) return false;

        CopySettingsFrom(SourceCamera(cam));
        bool isolated = PlaceRigFor(cam);
        ApplyState(cam, isolated);
        beforeRender?.Invoke(previewCam);

        previewCam.targetTexture = target;
        previewCam.aspect        = (float)target.width / target.height;

        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(previewCam, request))
            RenderPipeline.SubmitRenderRequest(previewCam, request);
        else
            previewCam.Render();

        return true;
    }

    // The game camera's aspect - what the shape of a preview should match. 16:9 when there is none.
    public static float GameAspect(CinemachineVirtualCameraBase cam)
    {
        var source = SourceCamera(cam);
        return source != null && source.aspect > 0.01f ? source.aspect : 16f / 9f;
    }

    // A texture sized in pixels for a rect sized in points - scaled displays draw more pixels than
    // points, so sizing it in points makes the view come out soft. Rebuilt only when the size changes.
    public static void EnsureTarget(ref RenderTexture target, Rect rect, string name)
    {
        float perPoint = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        int width  = Mathf.Clamp(Mathf.RoundToInt(rect.width  * perPoint), 4, MaxTargetSize);
        int height = Mathf.Clamp(Mathf.RoundToInt(rect.height * perPoint), 4, MaxTargetSize);

        if (target != null && target.width == width && target.height == height) return;

        ReleaseTarget(ref target);

        var readWrite = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Default;

        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, readWrite)
        {
            name         = name,
            hideFlags    = HideFlags.HideAndDontSave,
            antiAliasing = 1
        };
        target.Create();
    }

    public static void ReleaseTarget(ref RenderTexture target)
    {
        if (target == null) return;

        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        target = null;
    }

    public void Dispose()
    {
        if (previewCam != null)
        {
            previewCam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(previewCam.gameObject);
            previewCam = null;
        }

        cachedRendererSource = null;
        cachedRendererIndex  = -1;
    }

    // The hidden camera is disabled on purpose: it must never join the game's rendering, only render
    // when asked to. It is rebuilt whenever a scene load takes it away.
    void EnsureRig()
    {
        if (previewCam != null) return;

        var go = EditorUtility.CreateGameObjectWithHideFlags(
            "Cinemachine Preview Camera", HideFlags.HideAndDontSave, typeof(Camera));

        previewCam            = go.GetComponent<Camera>();
        previewCam.enabled    = false;
        previewCam.cameraType = CameraType.Game;
        previewCam.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
    }

    // Prefab Mode keeps the open prefab in a preview scene of its own, which cameras in the open level
    // cannot see - left there, the rig would frame the LEVEL from the prefab camera's position, which
    // is exactly the wrong picture. Moving it into that scene and pointing it at that scene (the same
    // pair of steps Unity's own PreviewRenderUtility takes) is what makes it render the prefab.
    // Returns true when the camera lives in such a scene.
    bool PlaceRigFor(CinemachineVirtualCameraBase cam)
    {
        var stage = PrefabStageUtility.GetPrefabStage(cam.gameObject);
        var scene = stage != null ? stage.scene : cam.gameObject.scene;
        bool isolated = scene.IsValid() && EditorSceneManager.IsPreviewScene(scene);

        if (scene.IsValid() && previewCam.gameObject.scene != scene)
            SceneManager.MoveGameObjectToScene(previewCam.gameObject, scene);

        previewCam.scene = isolated ? scene : default;
        return isolated;
    }

    static Camera SourceCamera(CinemachineVirtualCameraBase cam)
    {
        if (cam != null)
        {
            var brain = CinemachineCore.FindPotentialTargetBrain(cam);
            if (brain != null && brain.OutputCamera != null) return brain.OutputCamera;
        }
        return Camera.main;
    }

    // Matching the game camera's own settings is what makes this a preview of the game rather than of
    // a bare camera - same layers, same clear, same post stack and renderer.
    void CopySettingsFrom(Camera source)
    {
        if (source == null) return;

        previewCam.clearFlags          = source.clearFlags;
        previewCam.backgroundColor     = source.backgroundColor;
        previewCam.cullingMask         = source.cullingMask;
        previewCam.useOcclusionCulling = source.useOcclusionCulling;
        previewCam.allowHDR            = source.allowHDR;
        previewCam.allowMSAA           = source.allowMSAA;
        previewCam.depthTextureMode    = source.depthTextureMode;

        if (!source.TryGetComponent(out UniversalAdditionalCameraData sourceData)) return;

        var data = previewCam.GetUniversalAdditionalCameraData();
        data.renderType           = CameraRenderType.Base;
        data.renderShadows        = sourceData.renderShadows;
        data.renderPostProcessing = sourceData.renderPostProcessing;
        data.antialiasing         = sourceData.antialiasing;
        data.antialiasingQuality  = sourceData.antialiasingQuality;
        data.volumeLayerMask      = sourceData.volumeLayerMask;
        data.volumeTrigger        = sourceData.volumeTrigger;
        data.requiresDepthTexture = sourceData.requiresDepthTexture;
        data.requiresColorTexture = sourceData.requiresColorTexture;
        data.SetRenderer(RendererIndexOf(sourceData));
    }

    int RendererIndexOf(UniversalAdditionalCameraData data)
    {
        if (ReferenceEquals(data, cachedRendererSource)) return cachedRendererIndex;

        var property = new SerializedObject(data).FindProperty("m_RendererIndex");
        cachedRendererIndex  = property != null ? property.intValue : -1;
        cachedRendererSource = data;
        return cachedRendererIndex;
    }

    void ApplyState(CinemachineVirtualCameraBase cam, bool isolated)
    {
        // An enabled vcam has its state refreshed every frame by the brain, in edit mode as well as
        // play mode. A disabled one never updates - and neither does one in a prefab stage, where
        // there is no brain to drive it - so in both cases the transform is the only truth left.
        bool live  = cam.isActiveAndEnabled && !isolated;
        var  state = cam.State;

        var position = live ? state.GetFinalPosition()    : cam.transform.position;
        var rotation = live ? state.GetFinalOrientation() : cam.transform.rotation;
        previewCam.transform.SetPositionAndRotation(position, rotation);

        var lens = live ? state.Lens : LensOf(cam);
        if (lens.FieldOfView <= 0.01f) lens = LensSettings.Default;

        previewCam.orthographic          = lens.Orthographic;
        previewCam.nearClipPlane         = Mathf.Max(0.001f, lens.NearClipPlane);
        previewCam.farClipPlane          = Mathf.Max(previewCam.nearClipPlane + 0.01f, lens.FarClipPlane);
        previewCam.orthographicSize      = Mathf.Max(0.001f, lens.OrthographicSize);
        previewCam.usePhysicalProperties = lens.IsPhysicalCamera;

        if (lens.IsPhysicalCamera)
        {
            var physical = lens.PhysicalProperties;
            previewCam.sensorSize     = physical.SensorSize;
            previewCam.gateFit        = physical.GateFit;
            previewCam.focalLength    = Camera.FieldOfViewToFocalLength(lens.FieldOfView, physical.SensorSize.y);
            previewCam.lensShift      = physical.LensShift;
            previewCam.focusDistance  = physical.FocusDistance;
            previewCam.iso            = physical.Iso;
            previewCam.shutterSpeed   = physical.ShutterSpeed;
            previewCam.aperture       = physical.Aperture;
            previewCam.bladeCount     = physical.BladeCount;
            previewCam.curvature      = physical.Curvature;
            previewCam.barrelClipping = physical.BarrelClipping;
            previewCam.anamorphism    = physical.Anamorphism;
        }
        else
        {
            previewCam.fieldOfView = Mathf.Clamp(lens.FieldOfView, 1f, 179f);
        }
    }

    static LensSettings LensOf(CinemachineVirtualCameraBase cam)
        => cam is CinemachineCamera cinemachineCamera ? cinemachineCamera.Lens : LensSettings.Default;
}
