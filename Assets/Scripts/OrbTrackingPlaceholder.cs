using UnityEngine;

namespace Urp.ArDemo
{
    public sealed class OrbTrackingPlaceholder : MonoBehaviour
    {
        [SerializeField] private Transform trackedContentRoot;
        [SerializeField] private bool simulateTrackingInEditor = true;
        [SerializeField] private float editorOrbitSpeed = 8f;

        private void Update()
        {
            if (trackedContentRoot == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (simulateTrackingInEditor)
            {
                trackedContentRoot.Rotate(Vector3.up, editorOrbitSpeed * Time.deltaTime, Space.World);
            }
#endif
        }

        public void ApplyEstimatedPose(Vector3 position, Quaternion rotation)
        {
            if (trackedContentRoot == null)
            {
                return;
            }

            trackedContentRoot.SetPositionAndRotation(position, rotation);
        }
    }
}
