using UnityEngine;
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

        public void Setup(Material mat)
        {
            blitMaterial = mat;
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

            RenderGraphUtils.BlitMaterialParameters p = new(source, destination, blitMaterial, 0);
            renderGraph.AddBlitPass(p, passName: PassName);

            resourceData.cameraColor = destination;
        }
    }

    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
    public Material material;

    GlowFXPass pass;

    public override void Create()
    {
        pass = new GlowFXPass
        {
            renderPassEvent = injectionPoint,
            requiresIntermediateTexture = true
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null) return;
        pass.Setup(material);
        renderer.EnqueuePass(pass);
    }
}
