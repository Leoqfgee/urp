using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Urp.ArDemo
{
    /// <summary>
    /// Implements thesis section 3.4.1 with three independent off-screen
    /// buffers: complete damaged-bottle linear depth, original-cap linear
    /// depth, and original-cap lit colour. Only the final pixels are composed.
    /// </summary>
    public sealed class RepairOcclusionRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class FeatureSettings
        {
            public Material linearEyeDepthMaterial;
            public Material depthCompositeMaterial;
            public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        [SerializeField] private FeatureSettings settings = new FeatureSettings();
        private PaperDepthCompositePass pass;

        public FeatureSettings Settings => settings;

        public override void Create()
        {
            pass = new PaperDepthCompositePass(settings)
            {
                renderPassEvent = settings.passEvent
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (!PaperOcclusionRegistry.IsEnabled
                || renderingData.cameraData.camera != PaperOcclusionRegistry.Camera
                || settings.linearEyeDepthMaterial == null
                || settings.depthCompositeMaterial == null)
            {
                return;
            }
            renderer.EnqueuePass(pass);
        }

        public override void SetupRenderPasses(
            ScriptableRenderer renderer,
            in RenderingData renderingData)
        {
            if (pass != null)
            {
                pass.SetCameraColorTarget(renderer.cameraColorTargetHandle);
            }
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
        }

        private sealed class PaperDepthCompositePass : ScriptableRenderPass
        {
            private static readonly int BDepthId = Shader.PropertyToID("_PaperBDepthRT");
            private static readonly int CDepthId = Shader.PropertyToID("_PaperCDepthRT");
            private static readonly int CColorId = Shader.PropertyToID("_PaperCColorRT");
            private static readonly int EpsilonId =
                Shader.PropertyToID("_PaperOcclusionDepthEpsilonMeters");

            private readonly FeatureSettings settings;
            private RTHandle cameraColor;
            private RTHandle bDepth;
            private RTHandle cDepth;
            private RTHandle cColor;
            private RTHandle compositeScratch;
            private int diagnosticFrame;

            public PaperDepthCompositePass(FeatureSettings settings)
            {
                this.settings = settings;
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            public void SetCameraColorTarget(RTHandle target) => cameraColor = target;

            public override void OnCameraSetup(
                CommandBuffer cmd,
                ref RenderingData renderingData)
            {
                RenderTextureDescriptor depthDescriptor =
                    renderingData.cameraData.cameraTargetDescriptor;
                depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
                depthDescriptor.depthBufferBits = 32;
                depthDescriptor.msaaSamples = 1;
                depthDescriptor.bindMS = false;
                depthDescriptor.useMipMap = false;
                depthDescriptor.autoGenerateMips = false;
                RenderingUtils.ReAllocateIfNeeded(
                    ref bDepth,
                    depthDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_PaperBDepthRT");
                RenderingUtils.ReAllocateIfNeeded(
                    ref cDepth,
                    depthDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_PaperCDepthRT");

                RenderTextureDescriptor colorDescriptor =
                    renderingData.cameraData.cameraTargetDescriptor;
                colorDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                colorDescriptor.depthBufferBits = 32;
                colorDescriptor.msaaSamples = 1;
                colorDescriptor.bindMS = false;
                colorDescriptor.useMipMap = false;
                colorDescriptor.autoGenerateMips = false;
                RenderingUtils.ReAllocateIfNeeded(
                    ref cColor,
                    colorDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_PaperCColorRT");

                RenderTextureDescriptor scratchDescriptor = colorDescriptor;
                scratchDescriptor.depthBufferBits = 0;
                RenderingUtils.ReAllocateIfNeeded(
                    ref compositeScratch,
                    scratchDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_PaperCompositeScratch");
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                if (cameraColor == null || !PaperOcclusionRegistry.IsEnabled)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get("Paper 3.4.1 Occlusion");
                using (new ProfilingScope(
                           cmd,
                           new ProfilingSampler("Paper 3.4.1 Depth Composite")))
                {
                    DrawLinearDepth(
                        cmd,
                        bDepth,
                        PaperOcclusionRegistry.BottleRenderers);
                    DrawLinearDepth(
                        cmd,
                        cDepth,
                        PaperOcclusionRegistry.CapRenderers);
                    DrawCapColour(cmd, cColor, PaperOcclusionRegistry.CapRenderers);

                    cmd.SetGlobalTexture(BDepthId, bDepth);
                    cmd.SetGlobalTexture(CDepthId, cDepth);
                    cmd.SetGlobalTexture(CColorId, cColor);
                    cmd.SetGlobalFloat(
                        EpsilonId,
                        PaperOcclusionRegistry.DepthEpsilonMeters);
                    Blitter.BlitCameraTexture(
                        cmd,
                        cameraColor,
                        compositeScratch,
                        settings.depthCompositeMaterial,
                        0);
                    Blitter.BlitCameraTexture(cmd, compositeScratch, cameraColor);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                if ((Debug.isDebugBuild || Application.isEditor)
                    && (++diagnosticFrame % 120) == 0)
                {
                    Debug.Log(
                        "[URP_PAPER_OCCLUSION_DIAG] enabled=true "
                        + $"bRenderers={PaperOcclusionRegistry.BottleRenderers.Length} "
                        + $"cRenderers={PaperOcclusionRegistry.CapRenderers.Length} "
                        + "BDepth=R32_SFloat(m) CDepth=R32_SFloat(m) "
                        + $"epsilonMeters={PaperOcclusionRegistry.DepthEpsilonMeters:F6} "
                        + $"camera={renderingData.cameraData.camera.name}");
                }
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }

            public void Dispose()
            {
                bDepth?.Release();
                cDepth?.Release();
                cColor?.Release();
                compositeScratch?.Release();
            }

            private void DrawLinearDepth(
                CommandBuffer cmd,
                RTHandle target,
                Renderer[] renderers)
            {
                CoreUtils.SetRenderTarget(
                    cmd,
                    target,
                    ClearFlag.All,
                    new Color(1000f, 0f, 0f, 0f));
                foreach (Renderer renderer in renderers ?? Array.Empty<Renderer>())
                {
                    if (renderer == null)
                    {
                        continue;
                    }
                    int subMeshCount = GetSubMeshCount(renderer);
                    for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                    {
                        cmd.DrawRenderer(
                            renderer,
                            settings.linearEyeDepthMaterial,
                            subMesh,
                            0);
                    }
                }
            }

            private static void DrawCapColour(
                CommandBuffer cmd,
                RTHandle target,
                Renderer[] renderers)
            {
                CoreUtils.SetRenderTarget(
                    cmd,
                    target,
                    ClearFlag.All,
                    Color.clear);
                foreach (Renderer renderer in renderers ?? Array.Empty<Renderer>())
                {
                    if (renderer == null)
                    {
                        continue;
                    }
                    Material[] materials = renderer.sharedMaterials;
                    int subMeshCount = GetSubMeshCount(renderer);
                    for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                    {
                        Material material = materials.Length == 0
                            ? null
                            : materials[Mathf.Min(subMesh, materials.Length - 1)];
                        if (material != null)
                        {
                            // The exact original BottleCapC material is used.
                            cmd.DrawRenderer(renderer, material, subMesh, 0);
                        }
                    }
                }
            }

            private static int GetSubMeshCount(Renderer renderer)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                return mesh != null ? mesh.subMeshCount : 0;
            }
        }
    }
}
