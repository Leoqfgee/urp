using UnityEngine;
using UnityEngine.Rendering;
using Urp.ArDemo.Generated;
using Urp.ArDemo.Native;

namespace Urp.ArDemo
{
    public sealed class BuildIdentityRuntime : MonoBehaviour
    {
        public string DisplayText => BuildIdentity.Current.ShortText;

        private void Awake()
        {
            // The URP runtime debug UI can otherwise be opened by an accidental
            // touch gesture in a Development build and cover the AR page with
            // the "Display Stats" panel. Production APKs do not expose it.
            DebugManager.instance.enableRuntimeUI = false;

            BuildIdentityData identity = BuildIdentity.Current;
            string runtimeNative = NativeOrbTracker.BuildVersion;
            Debug.Log($"[BuildIdentity]\n{identity.ShortText}\nRuntime Native: {runtimeNative}");
            if (!string.Equals(identity.nativeBuildVersion, runtimeNative))
            {
                Debug.LogError(
                    $"[BuildIdentity] Native mismatch: embedded={identity.nativeBuildVersion}, runtime={runtimeNative}");
            }
        }
    }
}
