using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class DitheredEffectRenderFeature : ScriptableRendererFeature
{
    class DitherEffectPass : ScriptableRenderPass
    {
        const string PassName = "DitherEffectPass";
        private Material blitMaterial;

        public void Setup(Material material)
        {
            blitMaterial = material;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (blitMaterial == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // If URP resolved to back buffer, skip (cannot sample it)
            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogWarning(
                    "DitheredEffectRenderFeature skipped: camera is rendering directly to back buffer. " +
                    "Move the pass earlier in the pipeline."
                );
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = $"CameraColor-{PassName}";
            desc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(desc);

            RenderGraphUtils.BlitMaterialParameters blitParams =
                new(source, destination, blitMaterial, 0);

            renderGraph.AddBlitPass(blitParams, passName: PassName);

            // Feed result back into camera color for downstream passes
            resourceData.cameraColor = destination;
        }
    }

    // IMPORTANT: not AfterRenderingPostProcessing
    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRendering;
    public Material material;

    private DitherEffectPass pass;

    public override void Create()
    {
        pass = new DitherEffectPass
        {
            renderPassEvent = injectionPoint,
            requiresIntermediateTexture = true
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
            return;

        pass.Setup(material);
        renderer.EnqueuePass(pass);
    }
}
