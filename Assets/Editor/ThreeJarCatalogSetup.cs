#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    [InitializeOnLoad]
    public static class ThreeJarCatalogSetup
    {
        private const string CatalogPath = "Assets/Objects/RestorationObjectCatalog.asset";

        private sealed class Definition
        {
            public string id, folder, name, period, category, description;
            public string damagedModelPath, completeModelPath, thumbnailPath, afterImagePath;
            public string[] damagedTexturePaths, completeTexturePaths;
            public Color? completeSolidColor;
            public float height;
            public Vector3 viewerEuler;
            public Vector3 arEuler;
        }

        private static readonly Definition[] Definitions =
        {
            new Definition
            {
                id = "jar_nerfiller_restored_stl", folder = "BlackJar", name = "黑陶罐",
                period = "民间日用陶器", category = "黑陶储盛器",
                description = "这件黑陶罐为日用储盛陶器，整体器型饱满浑厚，鼓腹、束颈，口沿一侧保留残系耳，是典型的本土手工制陶遗存。器表呈深黑灰调，留存着清晰的绳纹与网格刻划纹饰，这件器物以泥料经高温烧制而成，纹饰兼具装饰与加固胎体的作用，是当时先民储存粮食、水酒的生活用具。它直观反映了彼时的制陶工艺水平，承载着区域先民的生产生活信息，为研究古代民间手工业与日常生活风貌提供珍贵实物参考。",
                damagedModelPath = "Assets/Models/Artifacts/BlackJar/BlackJarDamaged.fbx",
                completeModelPath = "Assets/Models/Artifacts/BlackJar/BlackJarRestored.obj",
                thumbnailPath = "Assets/Resources/Thumbnails/Jars/BlackJar.png",
                afterImagePath = "Assets/Resources/Thumbnails/Jars/BlackJarRestoredFinal.png",
                damagedTexturePaths = new[] { "Assets/Models/Artifacts/BlackJar/BlackJarTexture.png" },
                completeTexturePaths = new[] { "Assets/Models/Artifacts/BlackJar/BlackJarRestoredTexture.png" },
                height = .22f, viewerEuler = Vector3.zero, arEuler = Vector3.zero,
            },
            new Definition
            {
                id = "jar_brown_openmvs", folder = "BrownOpenMVS", name = "棕陶罐",
                period = "民间日用陶器", category = "素面陶质容器",
                description = "这件棕陶罐是民间日用陶质容器，器型规整，口沿外撇，短颈，圆鼓腹，平底，整体造型简约稳重。胎体呈浅棕红陶色，器表可见自然风化形成的斑驳白沁，表面无繁复彩绘，质朴素净，体现民间陶器实用优先的特征。它采用手工轮制成型，胎质朴实，是古人用来储存、盛放物资的日常器具。这类素面陶罐在古代民间广泛使用，制作门槛低、实用性强。器物留存的风化痕迹，记录了漫长埋藏环境带来的材质变化，是研究基层民众生活方式、民间制陶技术的直观实物标本，见证了乡土社会的生活图景。",
                damagedModelPath = "Assets/Models/Artifacts/BrownOpenMVS/BrownOpenMVS.fbx",
                completeModelPath = "Assets/Models/Artifacts/BrownOpenMVS/BrownOpenMVS.fbx",
                thumbnailPath = "Assets/Resources/Thumbnails/Jars/BrownOpenMVS.png",
                afterImagePath = "Assets/Resources/Thumbnails/Jars/BrownOpenMVS.png",
                damagedTexturePaths = new[] { "Assets/Models/Artifacts/BrownOpenMVS/BrownTexture0.jpg", "Assets/Models/Artifacts/BrownOpenMVS/BrownTexture1.jpg" },
                completeTexturePaths = new[] { "Assets/Models/Artifacts/BrownOpenMVS/BrownTexture0.jpg", "Assets/Models/Artifacts/BrownOpenMVS/BrownTexture1.jpg" },
                height = .20f, viewerEuler = Vector3.zero, arEuler = new Vector3(180f, 0f, 0f),
            },
            new Definition
            {
                id = "jar_blue_hybrid_restored", folder = "BlueHybridRestored", name = "蓝陶罐",
                period = "日用施釉陶器", category = "蓝釉陶质水器",
                description = "这件蓝釉陶罐为施釉陶质水器，器身修长，属于实用型盛器。罐身施蓝釉，釉面分布自然窑变白斑，釉色深浅错落，古朴富有变化，罐口有破损。釉陶相较于无釉陶器，密封性更好，可减少液体渗漏，多用于盛放水、酒等。这件器物融合了制胎与施釉技艺，反映出当时釉陶烧制技术的发展，兼具实用功能与朴素审美，是研究古代釉陶工艺、日用器具形制演变的重要实物资料。",
                damagedModelPath = "Assets/Models/Artifacts/BlueHybridRestored/BlueJarDamagedCardboard.fbx",
                completeModelPath = "Assets/Models/Artifacts/BlueHybridRestored/BlueJarRepairedFinal.fbx",
                thumbnailPath = "Assets/Resources/Thumbnails/Jars/BlueHybridRestored.png",
                afterImagePath = "Assets/Resources/Thumbnails/Jars/BlueRestoredFinal.png",
                damagedTexturePaths = new[] { "Assets/Models/Artifacts/BlueHybridRestored/BlueCardboardTexture.jpg", "Assets/Models/Artifacts/BlueHybridRestored/BlueJarTexture.jpg" },
                completeTexturePaths = new[] { "Assets/Models/Artifacts/BlueHybridRestored/BlueRepairedTexture.png" },
                height = .24f, viewerEuler = Vector3.zero, arEuler = new Vector3(90f, 0f, 0f),
            },
        };

        static ThreeJarCatalogSetup() => EditorApplication.delayCall += Configure;

        [MenuItem("Tools/URP AR/配置三个罐子模型目录")]
        public static void Configure()
        {
            RestorationObjectCatalog catalog = AssetDatabase.LoadAssetAtPath<RestorationObjectCatalog>(CatalogPath);
            if (catalog == null) return;
            List<ArtifactInfo> artifacts = new List<ArtifactInfo>(catalog.artifacts ?? Array.Empty<ArtifactInfo>());
            foreach (Definition definition in Definitions)
            {
                EnsureReadableModel(definition.damagedModelPath);
                EnsureReadableModel(definition.completeModelPath);
                GameObject damaged = AssetDatabase.LoadAssetAtPath<GameObject>(definition.damagedModelPath);
                GameObject complete = AssetDatabase.LoadAssetAtPath<GameObject>(definition.completeModelPath);
                Texture2D thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(definition.thumbnailPath);
                Texture2D afterImage = AssetDatabase.LoadAssetAtPath<Texture2D>(definition.afterImagePath);
                if (damaged == null || complete == null || thumbnail == null)
                {
                    Debug.LogWarning("[ThreeJarCatalog] Waiting for imports: " + definition.id);
                    EditorApplication.delayCall += Configure;
                    return;
                }
                string directory = "Assets/Models/Artifacts/" + definition.folder;
                Material[] damagedMaterials = BuildMaterials(directory, "Damaged", definition.damagedTexturePaths);
                Material[] completeMaterials = definition.completeSolidColor.HasValue
                    ? new[] { BuildSolidMaterial(directory, "CompleteCeramic", definition.completeSolidColor.Value) }
                    : BuildMaterials(directory, "Complete", definition.completeTexturePaths ?? definition.damagedTexturePaths);

                string profilePath = directory + "/" + definition.folder + "Profile.asset";
                RestorationObjectProfile profile = AssetDatabase.LoadAssetAtPath<RestorationObjectProfile>(profilePath);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<RestorationObjectProfile>();
                    AssetDatabase.CreateAsset(profile, profilePath);
                }
                profile.objectId = definition.id;
                profile.displayName = definition.name;
                profile.shortDescription = definition.category;
                profile.viewerDescription = definition.description;
                profile.trackingDescription = definition.description;
                profile.missingPartName = "缺损区域";
                profile.thumbnail = thumbnail;
                profile.introductionImage = thumbnail;
                profile.repairBeforeImage = thumbnail;
                profile.repairAfterImage = afterImage != null ? afterImage : thumbnail;
                profile.damagedViewerPrefab = damaged;
                profile.completeViewerPrefab = complete;
                profile.viewerMaterial = null;
                profile.damagedViewerMaterials = damagedMaterials;
                profile.completeViewerMaterials = completeMaterials;
                profile.defaultViewerEuler = definition.viewerEuler;
                profile.viewerMargin = .16f;
                EditorUtility.SetDirty(profile);

                string infoPath = directory + "/" + definition.folder + "Info.asset";
                ArtifactInfo info = AssetDatabase.LoadAssetAtPath<ArtifactInfo>(infoPath);
                if (info == null)
                {
                    info = ScriptableObject.CreateInstance<ArtifactInfo>();
                    AssetDatabase.CreateAsset(info, infoPath);
                }
                info.id = definition.id;
                info.displayName = definition.name;
                info.period = definition.period;
                info.category = definition.category;
                info.description = definition.description;
                info.defaultHeight = definition.height;
                info.thumbnail = thumbnail;
                info.supportsModelViewer = true;
                info.supportsArtifactAR = false;
                info.supportsOverlayAR = true;
                info.overlayProfile = profile;
                info.streamingAssetsModelPath = string.Empty;
                info.importedViewerPrefab = damaged;
                // The OpenMVS FBX files use the expected orientation in the isolated
                // viewer, but their reconstruction up axis is opposite Unity's AR
                // floor-plane up axis.  Keep viewer and AR corrections independent.
                info.importedModelEuler = definition.arEuler;
                info.importedModelMaterials = damagedMaterials;
                EditorUtility.SetDirty(info);

                int existing = artifacts.FindIndex(x => x != null && x.id == definition.id);
                if (existing >= 0) artifacts[existing] = info; else artifacts.Add(info);
            }
            catalog.artifacts = artifacts.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[ThreeJarCatalog] Three repair-aware jar models configured. Total artifacts=" + artifacts.Count);
        }

        public static void ValidateJarModelsFromCommandLine()
        {
            Configure();
            foreach (Definition definition in Definitions)
            {
                ValidateModel(definition.name + " 修复前", definition.damagedModelPath, definition.viewerEuler);
                ValidateModel(definition.name + " 修复后", definition.completeModelPath, definition.viewerEuler);
                ValidateModel(definition.name + " AR放置", definition.damagedModelPath, definition.arEuler);
            }
            Debug.Log("[ThreeJarCatalog][VALIDATION] all six AR jar model states are readable and measurable");
        }

        private static void ValidateModel(string label, string path, Vector3 euler)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException(label + " model missing: " + path);
            GameObject measurementRoot = new GameObject(label + " Validation Root");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.transform.SetParent(measurementRoot.transform, false);
                instance.transform.localRotation = Quaternion.Euler(euler);
                if (!ArtifactMeshGeometry.TryMeasure(measurementRoot.transform, out var measurement))
                    throw new InvalidOperationException(label + " has no readable visible vertices");
                Debug.Log($"[ThreeJarCatalog][VALIDATION] {label}: vertices={measurement.vertexCount}, bounds={measurement.bounds.size}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(measurementRoot);
            }
        }

        private static void EnsureReadableModel(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        private static Material[] BuildMaterials(string directory, string prefix, string[] texturePaths)
        {
            if (texturePaths == null || texturePaths.Length == 0) return Array.Empty<Material>();
            Material[] materials = new Material[texturePaths.Length];
            for (int index = 0; index < texturePaths.Length; index++)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePaths[index]);
                if (texture == null) continue;
                Material material = LoadOrCreateMaterial(directory + "/" + prefix + "Material" + index + ".mat");
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .18f);
                EditorUtility.SetDirty(material);
                materials[index] = material;
            }
            return materials;
        }

        private static Material BuildSolidMaterial(string directory, string name, Color color)
        {
            Material material = LoadOrCreateMaterial(directory + "/" + name + ".mat");
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string path)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }
    }
}
#endif
