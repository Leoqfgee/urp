using System;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;

namespace Urp.ArDemo
{
    public static class ArtifactModelLoader
    {
        public static async Task<GltfImport> Load(string relativePath)
        {
            string url = Application.streamingAssetsPath.TrimEnd('/') + "/" + relativePath.TrimStart('/');
            if (!url.Contains("://")) url = "file:///" + url;
            Debug.Log("[ArtifactModel] reading " + url);
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("GLB read failed: " + request.error + " (" + url + ")");
                byte[] bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException("GLB read returned no data: " + url);
                Debug.Log("[ArtifactModel] read bytes=" + bytes.Length);
                var import = new GltfImport();
                if (!await import.LoadGltfBinary(bytes))
                    throw new InvalidOperationException("GLB import failed: " + url);
                Debug.Log("[ArtifactModel] glTFast import success");
                return import;
            }
        }
    }
}
