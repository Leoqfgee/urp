using UnityEngine;

namespace Urp.ArDemo
{
    [CreateAssetMenu(menuName = "URP AR/Artifact Info")]
    public sealed class ArtifactInfo : ScriptableObject
    {
        public string id;
        public string displayName;
        public string period;
        public string category;
        [TextArea(3, 6)] public string description;
        public string streamingAssetsModelPath;
        public float defaultHeight = 0.22f;
        public Texture2D thumbnail;
        public bool supportsModelViewer;
        public bool supportsArtifactAR;
        public bool supportsOverlayAR;
        public RestorationObjectProfile overlayProfile;
        public GameObject importedViewerPrefab;
    }
}
