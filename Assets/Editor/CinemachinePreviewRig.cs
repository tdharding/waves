using System;
using System.Collections.Generic;
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

    // The watched camera's own post (its Cinemachine Volume Settings), worn for the one render. Switched off
    // the rest of the time, so the game and the Scene view never see it.
    Volume previewVolume;

    // The brain's own volumes carry whichever camera is LIVE - muted for the render, or every preview would
    // wear the live camera's depth of field. Their weights, to put back.
    readonly List<Volume> mutedVolumes = new List<Volume>();
    readonly List<float>  mutedWeights = new List<float>();

    // The Depth of Field whose Focus Distance was set for the render, and what it was.
    DepthOfField focusedDof;
    float        focusWas;
    bool         focusOverrideWas;

    // Renders the camera's view into the target. False when there is nothing to render with.
    // beforeRender is handed the hidden camera once it is standing where the view is, just before it draws.
    // focusOn, when set, is what the camera's depth of field focuses on (as a Camera Sequence's Focus On
    // View Target does at runtime), in place of its own Focus Tracking. post, when set, is the Volume Settings
    // worn in place of the camera's own - a Camera Influence camera is seen through the follow camera's post.
    // shotProfile, when set, is a Camera Sequence shot's own Volume, worn as the shot wears it: in place of the
    // settings' profile, Focus Tracking off, focused only by focusOn.
    public bool Render(CinemachineVirtualCameraBase cam, RenderTexture target, Action<Camera> beforeRender = null,
                       Transform focusOn = null, CinemachineVolumeSettings post = null, VolumeProfile shotProfile = null)
    {
        if (cam == null || target == null) return false;

        EnsureRig();
        if (previewCam == null) return false;

        var source = SourceCamera(cam);
        CopySettingsFrom(source);
        bool isolated = PlaceRigFor(cam);
        ApplyState(cam, isolated);

        try
        {
            WearPost(cam, source, focusOn, post, shotProfile);
            beforeRender?.Invoke(previewCam);

            previewCam.targetTexture = target;
            previewCam.aspect        = (float)target.width / target.height;

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(previewCam, request))
                RenderPipeline.SubmitRenderRequest(previewCam, request);
            else
                previewCam.Render();
        }
        finally
        {
            TakeOffPost();
        }

        return true;
    }

    // The ray from the last rendered view through a point on it (0-1 across, 0-1 up), and the layers that view
    // draws. False before anything has rendered.
    public bool TryViewportRay(Vector2 viewport, out Ray ray, out int cullingMask)
    {
        ray = default;
        cullingMask = 0;
        if (previewCam == null) return false;

        ray = previewCam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
        cullingMask = previewCam.cullingMask;
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
        previewVolume = null;

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

    // What the brain would put on the game camera with this camera live: its Volume Settings profile at its
    // Weight, with the focus worked out the way CinemachineVolumeSettings does it (distance from the camera
    // to the tracked thing, plus Focus Offset, written only into the Depth of Field's Focus Distance).
    void WearPost(CinemachineVirtualCameraBase cam, Camera source, Transform focusOn, CinemachineVolumeSettings post,
                  VolumeProfile shotProfile)
    {
        MuteBrainVolumes(source);

        var settings = post;
        if (settings == null) cam.TryGetComponent(out settings);
        if (settings == null || !settings.enabled) return;
        var profile = shotProfile != null ? shotProfile : settings.Profile;
        if (profile == null || profile.components.Count == 0) return;
        // Look At / Follow belong to the camera the settings are on.
        if (post != null && post.TryGetComponent(out CinemachineVirtualCameraBase owner)) cam = owner;

        var tracking = focusOn != null      ? CinemachineVolumeSettings.FocusTrackingMode.CustomTarget
                     : shotProfile != null  ? CinemachineVolumeSettings.FocusTrackingMode.None
                     : settings.FocusTracking;
        if (tracking != CinemachineVolumeSettings.FocusTrackingMode.None && profile.TryGet(out DepthOfField dof))
        {
            Transform tracked = null;
            switch (tracking)
            {
                case CinemachineVolumeSettings.FocusTrackingMode.LookAtTarget: tracked = cam.LookAt; break;
                case CinemachineVolumeSettings.FocusTrackingMode.FollowTarget: tracked = cam.Follow; break;
                case CinemachineVolumeSettings.FocusTrackingMode.CustomTarget:
                    tracked = focusOn != null ? focusOn : settings.FocusTarget; break;
            }

            // A shot's focus is the distance alone, as CameraSequence.FocusedCopy sets it.
            float distance = shotProfile != null ? 0f : settings.FocusOffset;
            if (tracked != null) distance += (previewCam.transform.position - tracked.position).magnitude;
            distance = Mathf.Max(0f, distance);

            // The profile is the shared asset - set for this render only, and put back straight after it.
            focusedDof       = dof;
            focusWas         = dof.focusDistance.value;
            focusOverrideWas = dof.focusDistance.overrideState;
            dof.focusDistance.value         = distance;
            dof.focusDistance.overrideState = true;
            if (previewCam.usePhysicalProperties) previewCam.focusDistance = distance;
        }

        if (previewVolume == null)
        {
            previewVolume = previewCam.gameObject.AddComponent<Volume>();
            previewVolume.enabled = false;
        }
        previewVolume.gameObject.layer = FirstVolumeLayer();
        previewVolume.isGlobal      = true;
        previewVolume.priority      = CinemachineVolumeSettings.s_VolumePriority;
        previewVolume.weight        = settings.Weight;
        previewVolume.sharedProfile = profile;
        previewVolume.enabled       = true;

        DebugFocus(cam, settings, profile, shotProfile, focusOn, tracking);
    }

    // [CmFocusDebug] — temporary: what the preview's depth of field is given, logged when it changes.
    static string s_lastFocusDebug;
    void DebugFocus(CinemachineVirtualCameraBase cam, CinemachineVolumeSettings settings, VolumeProfile profile,
                    VolumeProfile shotProfile, Transform focusOn, CinemachineVolumeSettings.FocusTrackingMode tracking)
    {
        string dofText = profile.TryGet(out DepthOfField d)
            ? $"dof active={d.active} mode={d.mode.value}(ov {d.mode.overrideState}) focus={d.focusDistance.value:F2}(ov {d.focusDistance.overrideState}) " +
              $"aperture={d.aperture.value} focal={d.focalLength.value}"
            : "NO DOF";
        var data = previewCam.GetUniversalAdditionalCameraData();
        string others = string.Empty;
        foreach (var v in VolumeManager.instance.GetVolumes(data.volumeLayerMask))
            if (v != null && v.isActiveAndEnabled && v != previewVolume)
                others += $" [{v.name} global={v.isGlobal} prio={v.priority} w={v.weight} profile={(v.sharedProfile != null ? v.sharedProfile.name : "null")}]";
        string line = $"[CmFocusDebug] '{cam.name}' shotProfile={(shotProfile != null ? shotProfile.name : "null")} " +
                      $"worn={profile.name} settingsProfile={(settings.Profile != null ? settings.Profile.name : "null")} " +
                      $"tracking={tracking} focusOn={(focusOn != null ? focusOn.name : "null")} " +
                      $"camPos={previewCam.transform.position} {dofText} post={data.renderPostProcessing} " +
                      $"volLayer={previewVolume.gameObject.layer} prio={previewVolume.priority} w={previewVolume.weight} others:{others}";
        // The distance moves a hair as the camera settles - log only real changes.
        string key = System.Text.RegularExpressions.Regex.Replace(line, @"camPos=\([^)]*\)", "");
        if (key == s_lastFocusDebug) return;
        s_lastFocusDebug = key;
        Debug.Log(line);
        s_stackLogFor = previewCam;
        RenderPipelineManager.endCameraRendering -= LogStack;
        RenderPipelineManager.endCameraRendering += LogStack;
    }

    // [CmFocusDebug] — what the volume stack held while the preview camera drew, once per logged change.
    static Camera s_stackLogFor;
    static void LogStack(ScriptableRenderContext context, Camera camera)
    {
        if (camera != s_stackLogFor) return;
        RenderPipelineManager.endCameraRendering -= LogStack;
        s_stackLogFor = null;
        var stack = VolumeManager.instance.stack;
        var dof   = stack != null ? stack.GetComponent<DepthOfField>() : null;
        var data  = camera.GetUniversalAdditionalCameraData();
        Debug.Log($"[CmFocusDebug] STACK while '{camera.name}' drew: " +
                  (dof != null ? $"active={dof.IsActive()} mode={dof.mode.value} focus={dof.focusDistance.value:F2} aperture={dof.aperture.value} focal={dof.focalLength.value}" : "no DoF in stack") +
                  $", depthTex={data.requiresDepthTexture} depthMode={camera.depthTextureMode} near={camera.nearClipPlane:F3} far={camera.farClipPlane:F1} " +
                  $"physical={camera.usePhysicalProperties} target={(camera.targetTexture != null ? $"{camera.targetTexture.width}x{camera.targetTexture.height}" : "none")}");
    }

    void TakeOffPost()
    {
        if (previewVolume != null) previewVolume.enabled = false;

        if (focusedDof != null)
        {
            focusedDof.focusDistance.value         = focusWas;
            focusedDof.focusDistance.overrideState = focusOverrideWas;
        }
        focusedDof = null;

        for (int i = 0; i < mutedVolumes.Count; i++)
            if (mutedVolumes[i] != null) mutedVolumes[i].weight = mutedWeights[i];
        mutedVolumes.Clear();
        mutedWeights.Clear();
    }

    // CinemachineVolumeSettings keeps the live camera's profiles on hidden volumes under the brain's camera.
    void MuteBrainVolumes(Camera source)
    {
        if (source == null) return;

        var t = source.transform;
        for (int c = 0; c < t.childCount; c++)
        {
            var child = t.GetChild(c);
            if (child.hideFlags != HideFlags.HideAndDontSave) continue;
            foreach (var volume in child.GetComponents<Volume>())
            {
                mutedVolumes.Add(volume);
                mutedWeights.Add(volume.weight);
                volume.weight = 0f;
            }
        }
    }

    // The volume has to sit on a layer the camera's Volume Mask sees - the first one, as Cinemachine picks.
    int FirstVolumeLayer()
    {
        int mask = previewCam.GetUniversalAdditionalCameraData().volumeLayerMask;
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    static LensSettings LensOf(CinemachineVirtualCameraBase cam)
        => cam is CinemachineCamera cinemachineCamera ? cinemachineCamera.Lens : LensSettings.Default;
}
