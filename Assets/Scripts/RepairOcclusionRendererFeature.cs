using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Urp.ArDemo
{
    /// <summary>
    /// v49 repair occlusion path. The complete registered B writes only to the
    /// main AR camera depth attachment immediately before opaque rendering.
    /// BottleCapC is not redrawn here: its original renderer/material continue
    /// through the ordinary URP ForwardLit path and standard ZTest supplies the
    /// view-dependent B/C visibility decision.
    /// </summary>
    public sealed class RepairOcclusionRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class FeatureSettings
        {
            public Material bottleDepthOnlyMaterial;
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        [SerializeField] private FeatureSettings settings = new FeatureSettings();
        private BottleRealObjectDepthPass depthPass;

        public FeatureSettings Settings => settings;

        public override void Create()
        {
            depthPass = new BottleRealObjectDepthPass(settings)
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
                || settings.bottleDepthOnlyMaterial == null)
            {
                return;
            }
            renderer.EnqueuePass(depthPass);
        }

        public override void SetupRenderPasses(
            ScriptableRenderer renderer,
            in RenderingData renderingData)
        {
            depthPass?.SetCameraTargets(
                renderer.cameraColorTargetHandle,
                renderer.cameraDepthTargetHandle);
        }

        private sealed class BottleRealObjectDepthPass : ScriptableRenderPass
        {
            private readonly FeatureSettings settings;
            private RTHandle cameraColor;
            private RTHandle cameraDepth;
            private int diagnosticFrame;

            public BottleRealObjectDepthPass(FeatureSettings settings)
            {
                this.settings = settings;
            }

            public void SetCameraTargets(RTHandle color, RTHandle depth)
            {
                cameraColor = color;
                cameraDepth = depth;
            }

            public override void OnCameraSetup(
                CommandBuffer cmd,
                ref RenderingData renderingData)
            {
                ConfigureTarget(cameraColor, cameraDepth);
                ConfigureClear(ClearFlag.None, Color.clear);
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                if (cameraColor == null
                    || cameraDepth == null
                    || !PaperOcclusionRegistry.IsEnabled)
                    return;

                Renderer[] renderers = PaperOcclusionRegistry.BottleRenderers;
                CommandBuffer cmd = CommandBufferPool.Get(
                    "Bottle B -> Main Camera Depth Before Cap ForwardLit");
                using (new ProfilingScope(
                           cmd,
                           new ProfilingSampler("Bottle Real Object Main Depth")))
                {
                    CoreUtils.SetRenderTarget(
                        cmd,
                        cameraColor,
                        cameraDepth,
                        ClearFlag.None,
                        Color.clear);
                    foreach (Renderer renderer in renderers ?? Array.Empty<Renderer>())
                    {
                        if (renderer == null)
                            continue;
                        int subMeshCount = GetSubMeshCount(renderer);
                        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                        {
                            cmd.DrawRenderer(
                                renderer,
                                settings.bottleDepthOnlyMaterial,
                                subMesh,
                                0);
                        }
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                if ((Debug.isDebugBuild || Application.isEditor)
                    && (++diagnosticFrame % 120) == 0)
                {
                    Debug.Log(
                        "[URP_BOTTLE_DEPTH_OCCLUSION_DIAG] enabled=true "
                        + $"bRenderers={renderers?.Length ?? 0} "
                        + "target=MainCameraDepth colorWrites=false "
                        + $"event={renderPassEvent} "
                        + "capPath=OriginalURPForwardLit cColorRT=false "
                        + $"camera={renderingData.cameraData.camera.name}");
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
