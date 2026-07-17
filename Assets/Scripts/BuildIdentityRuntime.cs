using UnityEngine;
using Urp.ArDemo.Generated;
using Urp.ArDemo.Native;

namespace Urp.ArDemo
{
    public sealed class BuildIdentityRuntime : MonoBehaviour
    {
        public string DisplayText => BuildIdentity.Current.ShortText;

        private void Awake()
        {
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
