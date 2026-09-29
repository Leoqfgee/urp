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
        [Tooltip("Extra rotation applied to directly imported OBJ/FBX models.")]
        public Vector3 importedModelEuler;
        [Tooltip("Optional materials used to replace the imported renderer slots in order.")]
        public Material[] importedModelMaterials;
    }
}
