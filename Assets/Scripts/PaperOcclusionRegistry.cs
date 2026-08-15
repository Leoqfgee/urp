using System;
using System.Collections.Generic;
using UnityEngine;

namespace Urp.ArDemo
{
    /// <summary>
    /// Runtime hand-off between the tracker and the URP paper-occlusion pass.
    /// It stores renderer references only. It never changes mesh data, material,
    /// hierarchy, or transforms of DamagedBottleB or BottleCapC.
    /// </summary>
    public static class PaperOcclusionRegistry
    {
        public const int BottleDepthOnlyLayer = 31;
        private static UnityEngine.Object owner;
        private static readonly Dictionary<GameObject, int> originalLayers =
            new Dictionary<GameObject, int>();
        private static int originalCameraCullingMask;
        private static bool cameraMaskCaptured;

        public static Camera Camera { get; private set; }
        public static Renderer[] BottleRenderers { get; private set; } =
            Array.Empty<Renderer>();
        public static bool IsEnabled { get; private set; }

        public static void Bind(
            UnityEngine.Object bindingOwner,
            Camera camera,
            Renderer[] completeDamagedBottleRenderers,
            Renderer[] unmodifiedCapRenderers,
            float depthEpsilonMeters)
        {
            owner = bindingOwner;
            Camera = camera;
            BottleRenderers = completeDamagedBottleRenderers ?? Array.Empty<Renderer>();
            IsEnabled = false;
        }

        public static void Enable(UnityEngine.Object bindingOwner)
        {
            if (owner != bindingOwner)
            {
                return;
            }

            RestoreDepthOnlyIsolation();
            if (Camera != null)
            {
                originalCameraCullingMask = Camera.cullingMask;
                cameraMaskCaptured = true;
                Camera.cullingMask &= ~(1 << BottleDepthOnlyLayer);
            }
            foreach (Renderer renderer in BottleRenderers ?? Array.Empty<Renderer>())
            {
                if (renderer == null) continue;
                GameObject target = renderer.gameObject;
                if (!originalLayers.ContainsKey(target))
                    originalLayers.Add(target, target.layer);
                target.layer = BottleDepthOnlyLayer;
                // DrawRenderer requires a live Renderer on Android.  The layer,
                // not renderer.enabled, keeps this same B geometry out of the
                // ordinary camera colour pass.
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
            }
            IsEnabled = true;
        }

        public static void Disable(UnityEngine.Object bindingOwner)
        {
            if (owner == bindingOwner)
            {
                IsEnabled = false;
                RestoreDepthOnlyIsolation();
            }
        }

        public static void Unbind(UnityEngine.Object bindingOwner)
        {
            if (owner != bindingOwner)
            {
                return;
            }
            IsEnabled = false;
            RestoreDepthOnlyIsolation();
            owner = null;
            Camera = null;
            BottleRenderers = Array.Empty<Renderer>();
        }

        private static void RestoreDepthOnlyIsolation()
        {
            foreach (KeyValuePair<GameObject, int> entry in originalLayers)
            {
                if (entry.Key != null)
                    entry.Key.layer = entry.Value;
            }
            originalLayers.Clear();
            if (cameraMaskCaptured && Camera != null)
                Camera.cullingMask = originalCameraCullingMask;
            cameraMaskCaptured = false;
        }
    }
}
