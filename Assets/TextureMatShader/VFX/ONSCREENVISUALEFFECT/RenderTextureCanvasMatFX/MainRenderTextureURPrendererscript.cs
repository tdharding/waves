using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class MainRenderTextureURPrendererscript : ScriptableRendererFeature
{
    [SerializeField]
    private MainRenderTextureURPrendererscriptSettings settings;

    private MainRenderTextureURPrendererscriptPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new MainRenderTextureURPrendererscriptPass(settings);
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }

    // ================= SETTINGS =================

    [Serializable]
    public class MainRenderTextureURPrendererscriptSettings
    {
        public Material fullscreenMaterial;
    }

    // ================= PASS =================

    public class MainRenderTextureURPrendererscriptPass : ScriptableRenderPass
    {
        private readonly MainRenderTextureURPrendererscriptSettings settings;

        public MainRenderTextureURPrendererscriptPass(MainRenderTextureURPrendererscriptSettings settings)
        {
            this.settings = settings;
        }

        private class PassData
        {
            public Material material;
        }

        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.material == null)
                return;

            context.cmd.DrawProcedural(
                Matrix4x4.identity,
                data.material,
                0,
                MeshTopology.Triangles,
                3
            );
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "Fullscreen Shader Graph Pass";

            using (var builder =
                   renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                passData.material = settings.fullscreenMaterial;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    ExecutePass(data, context));
            }
        }
    }
}
