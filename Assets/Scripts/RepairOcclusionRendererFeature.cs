using System;
using System.IO;
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
        private PaperBackgroundCapturePass backgroundPass;
        private PaperDepthCompositePass pass;

        public FeatureSettings Settings => settings;

        public override void Create()
        {
            backgroundPass = new PaperBackgroundCapturePass
            {
                renderPassEvent = (RenderPassEvent)
                    ((int)RenderPassEvent.BeforeRenderingOpaques + 1)
            };
            pass = new PaperDepthCompositePass(settings, backgroundPass)
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
            renderer.EnqueuePass(backgroundPass);
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
            backgroundPass?.SetCameraColorTarget(renderer.cameraColorTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            backgroundPass?.Dispose();
            pass?.Dispose();
        }

        private sealed class PaperBackgroundCapturePass : ScriptableRenderPass
        {
            private RTHandle cameraColor;
            private RTHandle background;
            public RTHandle Background => background;

            public void SetCameraColorTarget(RTHandle target) => cameraColor = target;

            public override void OnCameraSetup(
                CommandBuffer cmd,
                ref RenderingData renderingData)
            {
                RenderTextureDescriptor descriptor =
                    renderingData.cameraData.cameraTargetDescriptor;
                descriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                RenderingUtils.ReAllocateIfNeeded(
                    ref background,
                    descriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_PaperCameraBackgroundRT");
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                if (cameraColor == null || background == null) return;
                CommandBuffer cmd = CommandBufferPool.Get(
                    "Paper Capture AR Background Before Original C");
                Blitter.BlitCameraTexture(cmd, cameraColor, background);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose() => background?.Release();
        }

        private sealed class PaperDepthCompositePass : ScriptableRenderPass
        {
            private static readonly int BDepthId = Shader.PropertyToID("_PaperBDepthRT");
            private static readonly int CDepthId = Shader.PropertyToID("_PaperCDepthRT");
            private static readonly int CColorId = Shader.PropertyToID("_PaperCColorRT");
            private static readonly int EpsilonId =
                Shader.PropertyToID("_PaperOcclusionDepthEpsilonMeters");

            private readonly FeatureSettings settings;
            private readonly PaperBackgroundCapturePass backgroundPass;
            private RTHandle cameraColor;
            private RTHandle bDepth;
            private RTHandle cDepth;
            private RTHandle cColor;
            private RTHandle occlusionMask;
            private RTHandle compositeScratch;
            private int diagnosticFrame;

            public PaperDepthCompositePass(
                FeatureSettings settings,
                PaperBackgroundCapturePass backgroundPass)
            {
                this.settings = settings;
                this.backgroundPass = backgroundPass;
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
                colorDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB;
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
                RenderingUtils.ReAllocateIfNeeded(
                    ref occlusionMask,
                    colorDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_PaperOcclusionMaskRT");

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
                RTHandle cameraBackground = backgroundPass.Background;
                if (cameraColor == null
                    || cameraBackground == null
                    || !PaperOcclusionRegistry.IsEnabled)
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
                    cmd.SetGlobalTexture(CDepthId, cDepth);
                    // C was rendered normally by the main URP camera. Extract
                    // its already-lit pixels using CDepth instead of manually
                    // replaying a Lit pass, which caused the v47 Android red cap.
                    Blitter.BlitCameraTexture(
                        cmd,
                        cameraColor,
                        cColor,
                        settings.depthCompositeMaterial,
                        2);

                    cmd.SetGlobalTexture(BDepthId, bDepth);
                    cmd.SetGlobalTexture(CColorId, cColor);
                    cmd.SetGlobalFloat(
                        EpsilonId,
                        PaperOcclusionRegistry.DepthEpsilonMeters);
                    Blitter.BlitCameraTexture(
                        cmd,
                        cameraBackground,
                        compositeScratch,
                        settings.depthCompositeMaterial,
                        0);
                    Blitter.BlitCameraTexture(
                        cmd,
                        cameraBackground,
                        occlusionMask,
                        settings.depthCompositeMaterial,
                        1);
                    Blitter.BlitCameraTexture(cmd, compositeScratch, cameraColor);

                    if (PaperOcclusionRegistry.ConsumeCaptureRequest(
                            out string captureDirectory))
                    {
                        bool projectionFlipped =
                            renderingData.cameraData.IsCameraProjectionMatrixFlipped();
                        RuntimeBufferCapture.Enqueue(
                            cmd,
                            captureDirectory,
                            cameraBackground,
                            bDepth,
                            cDepth,
                            cColor,
                            occlusionMask,
                            compositeScratch,
                            projectionFlipped);
                    }
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
                        + $"camera={renderingData.cameraData.camera.name} "
                        + $"graphicsUVStartsAtTop={SystemInfo.graphicsUVStartsAtTop} "
                        + "sharedScreenConvention=true");
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
                occlusionMask?.Release();
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

            private static int GetSubMeshCount(Renderer renderer)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                return mesh != null ? mesh.subMeshCount : 0;
            }
        }

        private static class RuntimeBufferCapture
        {
            public static void Enqueue(
                CommandBuffer cmd,
                string directory,
                RTHandle background,
                RTHandle bottleDepth,
                RTHandle capDepth,
                RTHandle capColor,
                RTHandle mask,
                RTHandle finalComposite,
                bool projectionFlipped)
            {
                Directory.CreateDirectory(directory);
                RequestColor(cmd, background, Path.Combine(directory, "CameraBackground.png"));
                RequestDepth(cmd, bottleDepth, Path.Combine(directory, "BDepth.exr"));
                RequestDepth(cmd, capDepth, Path.Combine(directory, "CDepth.exr"));
                RequestColor(
                    cmd,
                    capColor,
                    Path.Combine(directory, "CColor.png"),
                    true);
                RequestColor(cmd, mask, Path.Combine(directory, "OcclusionMask.png"));
                RequestColor(
                    cmd,
                    finalComposite,
                    Path.Combine(directory, "FinalComposite.png"));
                File.WriteAllText(
                    Path.Combine(directory, "screen_convention.txt"),
                    $"graphicsUVStartsAtTop={SystemInfo.graphicsUVStartsAtTop}\n"
                    + $"cameraProjectionFlipped={projectionFlipped}\n"
                    + "BDepth/CDepth/CColor target descriptors and RTHandle viewport are identical.\n");
                Debug.Log("[URP_V48_CAP_COLOR_DIAG] runtime capture queued path="
                    + directory);
            }

            private static void RequestColor(
                CommandBuffer cmd,
                RTHandle source,
                string path,
                bool logCapStats = false)
            {
                int width = source.rt.width;
                int height = source.rt.height;
                cmd.RequestAsyncReadback(source.rt, 0, request =>
                {
                    if (request.hasError)
                    {
                        Debug.LogError("[URP_V48_CAP_COLOR_DIAG] readback failed " + path);
                        return;
                    }
                    Texture2D texture = new Texture2D(
                        width,
                        height,
                        TextureFormat.RGBA32,
                        false,
                        false);
                    texture.LoadRawTextureData(request.GetData<byte>());
                    texture.Apply(false, false);
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                    if (logCapStats)
                    {
                        LogCapStats(texture, path);
                        PaperOcclusionRegistry.ReportPaperColorStats(texture);
                    }
                    UnityEngine.Object.Destroy(texture);
                });
            }

            private static void RequestDepth(
                CommandBuffer cmd,
                RTHandle source,
                string path)
            {
                int width = source.rt.width;
                int height = source.rt.height;
                cmd.RequestAsyncReadback(source.rt, 0, request =>
                {
                    if (request.hasError)
                    {
                        Debug.LogError("[URP_V48_CAP_COLOR_DIAG] readback failed " + path);
                        return;
                    }
                    Texture2D texture = new Texture2D(
                        width,
                        height,
                        TextureFormat.RFloat,
                        false,
                        true);
                    texture.LoadRawTextureData(request.GetData<byte>());
                    texture.Apply(false, false);
                    File.WriteAllBytes(
                        path,
                        texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                    UnityEngine.Object.Destroy(texture);
                });
            }

            private static void LogCapStats(Texture2D texture, string path)
            {
                Color32[] pixels = texture.GetPixels32();
                long r = 0;
                long g = 0;
                long b = 0;
                int count = 0;
                int minX = texture.width;
                int minY = texture.height;
                int maxX = -1;
                int maxY = -1;
                long centroidX = 0;
                long centroidY = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 pixel = pixels[index];
                    if (pixel.a == 0) continue;
                    int x = index % texture.width;
                    int y = index / texture.width;
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    count++;
                    centroidX += x;
                    centroidY += y;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
                Debug.Log(
                    "[URP_V48_CAP_COLOR_DIAG] "
                    + $"path={path} alphaPixels={count} "
                    + $"meanRGB=({(count > 0 ? r / (255f * count) : 0f):F4},"
                    + $"{(count > 0 ? g / (255f * count) : 0f):F4},"
                    + $"{(count > 0 ? b / (255f * count) : 0f):F4}) "
                    + $"bounds=({minX},{minY})-({maxX},{maxY}) "
                    + $"centroid=({(count > 0 ? centroidX / (float)count : -1f):F2},"
                    + $"{(count > 0 ? centroidY / (float)count : -1f):F2}) "
                    + "materialPass=ForwardLit diagnosticReplacement=false");
            }
        }
    }
}
