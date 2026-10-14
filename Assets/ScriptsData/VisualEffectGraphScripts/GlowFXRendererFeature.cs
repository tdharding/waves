using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class GlowFXRendererFeature : ScriptableRendererFeature
{
    class GlowFXPass : ScriptableRenderPass
    {
        const string PassName = "GlowFXPass";
        Material blitMaterial;

        // Angel glow: everything on glowLayer drawn white into a silhouette, which GlowFXMask.hlsl
        // blurs into the outer glow. Only drawn while BrightnessGlowController has the glow up.
        Material silhouetteMaterial;
        uint glowLayer;

        static readonly int AngelGlowMaskPID    = Shader.PropertyToID("_AngelGlowMask");
        static readonly int AngelGlowOpacityPID = Shader.PropertyToID("_AngelGlowOpacity");

        static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        class SilhouettePassData
        {
            public RendererListHandle renderers;
        }

        public void Setup(Material mat, Material silhouette, uint layer)
        {
            blitMaterial = mat;
            silhouetteMaterial = silhouette;
            glowLayer = layer;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (blitMaterial == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = $"CameraColor-{PassName}";
            desc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(desc);

            TextureHandle silhouette = RecordSilhouette(renderGraph, frameData, resourceData, desc);

            RenderGraphUtils.BlitMaterialParameters p = new(source, destination, blitMaterial, 0);
            using (var builder = renderGraph.AddBlitPass(p, passName: PassName, returnBuilder: true))
            {
                if (silhouette.IsValid())
                    builder.UseGlobalTexture(AngelGlowMaskPID);
            }

            resourceData.cameraColor = destination;
        }

        TextureHandle RecordSilhouette(RenderGraph renderGraph, ContextContainer frameData,
                                       UniversalResourceData resourceData, TextureDesc colorDesc)
        {
            if (silhouetteMaterial == null || glowLayer == 0) return TextureHandle.nullHandle;
            if (Shader.GetGlobalFloat(AngelGlowOpacityPID) <= 0f) return TextureHandle.nullHandle;

            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData    = frameData.Get<UniversalCameraData>();
            var lightData     = frameData.Get<UniversalLightData>();

            var desc = colorDesc;
            desc.name        = "AngelGlowSilhouette";
            desc.format      = GraphicsFormat.R8_UNorm;
            desc.filterMode  = FilterMode.Bilinear;
            desc.wrapMode    = TextureWrapMode.Clamp;
            desc.clearBuffer = true;
            desc.clearColor  = Color.clear;
            TextureHandle silhouette = renderGraph.CreateTexture(desc);

            var drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData,
                                                               SortingCriteria.None);
            drawing.overrideMaterial = silhouetteMaterial;
            drawing.overrideMaterialPassIndex = 0;

            var filtering = new FilteringSettings(RenderQueueRange.all, ~0, glowLayer);

            using (var builder = renderGraph.AddRasterRenderPass<SilhouettePassData>("AngelGlowSilhouette", out var data))
            {
                data.renderers = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, drawing, filtering));

                builder.UseRendererList(data.renderers);
                builder.SetRenderAttachment(silhouette, 0);
                // Tested against the scene's depth, so a rock in front of the angel cuts the glow too.
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(silhouette, AngelGlowMaskPID);
                builder.SetRenderFunc((SilhouettePassData d, RasterGraphContext ctx) =>
                    ctx.cmd.DrawRendererList(d.renderers));
            }

            return silhouette;
        }
    }

    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
    public Material material;

    [Header("Angel Glow")]
    [Tooltip("Renderers on this rendering layer get the outer glow. Defaults to the ninth rendering " +
             "layer — add it as \"Glow\" under Project Settings > Tags and Layers > Rendering Layers.")]
    public RenderingLayerMask glowLayer = 1u << 8;

    [Tooltip("Draws the glowing renderers' silhouette. Filled in automatically with Hidden/AngelGlowSilhouette.")]
    public Shader silhouetteShader;

    GlowFXPass pass;
    Material silhouetteMaterial;

    public override void Create()
    {
        pass = new GlowFXPass
        {
            renderPassEvent = injectionPoint,
            requiresIntermediateTexture = true
        };

#if UNITY_EDITOR
        if (silhouetteShader == null)
        {
            silhouetteShader = Shader.Find("Hidden/AngelGlowSilhouette");
            if (silhouetteShader != null) UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        if (silhouetteShader != null)
            silhouetteMaterial = CoreUtils.CreateEngineMaterial(silhouetteShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null) return;
        pass.Setup(material, silhouetteMaterial, glowLayer);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(silhouetteMaterial);
        silhouetteMaterial = null;
    }
}
