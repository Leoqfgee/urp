using System;
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
        private static UnityEngine.Object owner;

        public static Camera Camera { get; private set; }
        public static Renderer[] BottleRenderers { get; private set; } =
            Array.Empty<Renderer>();
        public static Renderer[] CapRenderers { get; private set; } =
            Array.Empty<Renderer>();
        public static float DepthEpsilonMeters { get; private set; } = 0.0005f;
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
            CapRenderers = unmodifiedCapRenderers ?? Array.Empty<Renderer>();
            DepthEpsilonMeters = Mathf.Max(0f, depthEpsilonMeters);
            IsEnabled = false;
        }

        public static void Enable(UnityEngine.Object bindingOwner)
        {
            if (owner == bindingOwner)
            {
                IsEnabled = true;
            }
        }

        public static void Disable(UnityEngine.Object bindingOwner)
        {
            if (owner == bindingOwner)
            {
                IsEnabled = false;
            }
        }

        public static void Unbind(UnityEngine.Object bindingOwner)
        {
            if (owner != bindingOwner)
            {
                return;
            }
            owner = null;
            Camera = null;
            BottleRenderers = Array.Empty<Renderer>();
            CapRenderers = Array.Empty<Renderer>();
            IsEnabled = false;
        }
    }
}
