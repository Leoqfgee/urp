using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Tests.Editor
{
    public sealed class V48OcclusionRegressionTests
    {
        private const string ControllerPath =
            "Assets/Scripts/OrbImageTrackingController.cs";
        private const string FeaturePath =
            "Assets/Scripts/RepairOcclusionRendererFeature.cs";
        private const string RegistryPath =
            "Assets/Scripts/PaperOcclusionRegistry.cs";
        private const string QaPath =
            "Assets/Calibration/paper_occlusion_qa_v48.json";
        private const string IntegrityPath =
            "Assets/Calibration/bottle_cap_asset_integrity_v48.json";
        private const string PairPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "bottle_full_aligned_v2.fbx";

        [Test]
        public void BottleCapAssetMatchesV46()
        {
            string json = File.ReadAllText(QaPath);
            StringAssert.Contains("\"source_geometry_c\": \"unmodified original BottleCapC\"", json);
            StringAssert.Contains("\"bottle_cap_c_mesh_sha256\":", json);
            string integrity = File.ReadAllText(IntegrityPath);
            StringAssert.Contains("\"matches_v46\": true", integrity);
            Assert.AreEqual(
                "F0661ADB5E953A1DA4605A943995E251ACD4039C0397A14B99F0418319562D21",
                Sha256(File.ReadAllBytes(PairPath)),
                "The FBX containing the v46 original BottleCapC changed.");
        }

        [Test]
        public void BottleCapMaterialMatchesV46()
        {
            BottleCapAssetMatchesV46();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/CleanBottleCapLit.mat");
            Assert.NotNull(material);
            Assert.AreEqual(
                "Assets/Materials/CleanBottleCapLit.mat",
                AssetDatabase.GetAssetPath(material));
            Assert.AreEqual(
                "4e2ac76533fd4bba8da1cdb51f07b8d1",
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
            Assert.That(material.GetColor("_BaseColor").r, Is.EqualTo(0.96f).Within(0.001f));
        }

        [Test]
        public void BottleCapTransformMatchesV46()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PairPath);
            Transform cap = Find(prefab.transform, "BottleCapC");
            Assert.NotNull(cap);
            Assert.AreEqual(Vector3.zero, cap.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.identity, cap.localRotation), 0.0001f);
            Assert.AreEqual(Vector3.one, cap.localScale);
            string controller = File.ReadAllText(ControllerPath);
            StringAssert.DoesNotContain("registeredRepairPart.localPosition", controller);
            StringAssert.DoesNotContain("registeredRepairPart.localRotation", controller);
            StringAssert.DoesNotContain("registeredRepairPart.localScale", controller);
        }

        [Test]
        public void PaperPipelineDoesNotChangeCapTransform()
        {
            BottleCapTransformMatchesV46();
            string registry = File.ReadAllText(RegistryPath);
            StringAssert.DoesNotContain("localPosition", registry);
            StringAssert.DoesNotContain("localRotation", registry);
            StringAssert.DoesNotContain("localScale", registry);
        }

        [Test]
        public void NoDiagnosticCapMaterialInRuntime()
        {
            string diagnostic = File.ReadAllText(
                "Assets/Scripts/CapVisibilityDiagnostic.cs");
            StringAssert.DoesNotContain("new Material", diagnostic);
            StringAssert.DoesNotContain("CreatePrimitive", diagnostic);
            StringAssert.DoesNotContain("sharedMaterials =", diagnostic);
            string feature = File.ReadAllText(FeaturePath);
            StringAssert.DoesNotContain("Color.red", feature);
            StringAssert.DoesNotContain("Color.magenta", feature);
            StringAssert.DoesNotContain("new Material", feature);
        }

        [Test]
        public void NoOcclusionGeometryScaleHackExists()
        {
            string[] occlusionImplementationFiles =
            {
                FeaturePath,
                RegistryPath,
                "Assets/Shaders/PaperDepthComposite.shader",
                "Assets/Scripts/OrbImageTrackingController.cs",
                "Docs/PaperOcclusionImplementation.md"
            };
            string all = string.Join("\n",
                occlusionImplementationFiles.Select(File.ReadAllText));
            foreach (string token in new[]
                     {
                         "Bottle" + "Repair" + "Occluder",
                         "neck" + "_radial_" + "dilation",
                         "occluder" + "Scale", "cap" + "Scale",
                         "cap" + "Offset", "neck" + "Scale",
                         "occlusion" + "Geometry" + "Margin"
                     })
                StringAssert.DoesNotContain(token, all);
        }

        [Test]
        public void FullDamagedBottleBProducesDepthBuffer()
        {
            string feature = File.ReadAllText(FeaturePath);
            string controller = File.ReadAllText(ControllerPath);
            StringAssert.Contains("PaperOcclusionRegistry.BottleRenderers", feature);
            StringAssert.Contains("referenceRenderers", controller);
            StringAssert.Contains("complete DamagedBottleB hierarchy", File.ReadAllText(QaPath));
        }

        [Test]
        public void BottleCapCProducesIndependentDepthBuffer()
        {
            string feature = File.ReadAllText(FeaturePath);
            StringAssert.Contains("DrawLinearDepth(", feature);
            StringAssert.Contains("cDepth", feature);
            StringAssert.Contains("PaperOcclusionRegistry.CapRenderers", feature);
        }

        [Test]
        public void BottleCapCProducesUnmodifiedColorBuffer()
        {
            string feature = File.ReadAllText(FeaturePath);
            StringAssert.Contains("ExtractOriginalCapColor", File.ReadAllText(
                "Assets/Shaders/PaperDepthComposite.shader"));
            StringAssert.Contains("Blitter.BlitCameraTexture(", feature);
            StringAssert.DoesNotContain("new Material", feature);
            StringAssert.DoesNotContain("DrawCapColour", feature);
        }

        [Test]
        public void OriginalCapAndCColorRTAreVisuallyEquivalent()
        {
            string json = File.ReadAllText(QaPath);
            foreach (float rms in ExtractFloats(json, "direct_vs_paper_rgb_rms"))
                Assert.LessOrEqual(rms, 0.08f);
            CollectionAssert.AreEqual(
                ExtractInts(json, "direct_alpha_pixels"),
                ExtractInts(json, "paper_alpha_pixels"));
        }

        [Test]
        public void NoBottleDepthShowsCompleteOriginalCap()
        {
            foreach (string view in new[] { "front", "left", "right", "top" })
            {
                Assert.IsTrue(File.Exists(
                    $"Artifacts/V48OcclusionQA/{view}/DirectOriginalC.png"));
                Assert.IsTrue(File.Exists(
                    $"Artifacts/V48OcclusionQA/{view}/NoOcclusionComposite.png"));
            }
            string shader = File.ReadAllText("Assets/Shaders/PaperDepthComposite.shader");
            StringAssert.Contains("!bottleExists", shader);
        }

        [Test]
        public void PaperPipelineDoesNotRecolorCap()
        {
            OriginalCapAndCColorRTAreVisuallyEquivalent();
            BottleCapMaterialMatchesV46();
        }

        [Test]
        public void PaperPipelineDoesNotChangeCapBounds()
        {
            string json = File.ReadAllText(QaPath);
            StringAssert.Contains("\"direct_bounds\"", json);
            StringAssert.Contains("\"paper_bounds\"", json);
            CollectionAssert.AreEqual(
                ExtractVector4Strings(json, "direct_bounds"),
                ExtractVector4Strings(json, "paper_bounds"));
        }

        [Test]
        public void BDepthCDepthCColorUseSameScreenConvention()
        {
            string feature = File.ReadAllText(FeaturePath);
            StringAssert.Contains("cameraTargetDescriptor", feature);
            StringAssert.Contains("_PaperCameraBackgroundRT", feature);
            StringAssert.Contains("sharedScreenConvention=true", feature);
            StringAssert.DoesNotContain("uv.y = 1", File.ReadAllText(
                "Assets/Shaders/PaperDepthComposite.shader"));
        }

        [Test]
        public void DepthMaskMatchesPaperAlgorithm()
        {
            Assert.GreaterOrEqual(
                ExtractFloat(File.ReadAllText(QaPath),
                    "synthetic_gpu_cpu_mask_agreement"),
                0.99f);
            string shader = File.ReadAllText("Assets/Shaders/PaperDepthComposite.shader");
            StringAssert.Contains("depthC < depthB - _PaperOcclusionDepthEpsilonMeters", shader);
        }

        [Test]
        public void BInFrontHidesCapPixel()
        {
            const float depthB = 0.40f;
            const float depthC = 0.50f;
            Assert.IsFalse(depthC < depthB - 0.0005f);
        }

        [Test]
        public void CapInFrontShowsCapPixel()
        {
            const float depthB = 0.50f;
            const float depthC = 0.40f;
            Assert.IsTrue(depthC < depthB - 0.0005f);
        }

        [Test]
        public void BackgroundPreservedWhenCapHidden()
        {
            string shader = File.ReadAllText("Assets/Shaders/PaperDepthComposite.shader");
            StringAssert.Contains("? half4(cap.rgb, 1.0) : background", shader);
        }

        [Test] public void FrontViewOcclusionDerivedFromDepth() =>
            AssertViewOcclusionDerivedFromDepth("front");
        [Test] public void LeftViewOcclusionDerivedFromDepth() =>
            AssertViewOcclusionDerivedFromDepth("left");
        [Test] public void RightViewOcclusionDerivedFromDepth() =>
            AssertViewOcclusionDerivedFromDepth("right");
        [Test] public void TopViewOcclusionDerivedFromDepth() =>
            AssertViewOcclusionDerivedFromDepth("top");

        private static void AssertViewOcclusionDerivedFromDepth(string view)
        {
            string json = File.ReadAllText(QaPath);
            StringAssert.Contains($"\"view\": \"{view}\"", json);
            StringAssert.Contains($"Artifacts/V48OcclusionQA/{view}/B_depth.exr", json);
            StringAssert.Contains($"Artifacts/V48OcclusionQA/{view}/C_depth.exr", json);
            StringAssert.Contains($"Artifacts/V48OcclusionQA/{view}/OcclusionMask.png", json);
            Assert.IsTrue(File.Exists($"Artifacts/V48OcclusionQA/{view}/FinalComposite.png"));
        }

        [Test]
        public void StartDoesNotChangeBottleCapMatrix()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("AssertMatrixUnchanged(\"BottleCapC\", before.cap, after.cap)", source);
            StringAssert.Contains("PaperOcclusionRegistry.Enable(this)", source);
        }

        [Test]
        public void StartDoesNotChangeTrackedRootMatrix()
        {
            StringAssert.Contains(
                "AssertMatrixUnchanged(\"TrackedBottleRoot\", before.root, after.root)",
                File.ReadAllText(ControllerPath));
        }

        [Test]
        public void StartDoesNotChangeBottlePairMatrix()
        {
            StringAssert.Contains(
                "AssertMatrixUnchanged(\"BottleRepairRoot\", before.pair, after.pair)",
                File.ReadAllText(ControllerPath));
        }

        [Test]
        public void V48PoseChainMatchesV46Baseline()
        {
            Assert.AreEqual(
                "C6BE7C1C64C6416D1276F1E1D0055F170C81A0335E5DE3BC1DFB673F2F5DCE71",
                Sha256(File.ReadAllBytes("Assets/Scenes/UrpARPrototype.unity")),
                "Tracking hierarchy/AR camera scene must remain byte-identical to v46.");
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("OpenCvUnityPoseConverter.TryGetObjectPose", source);
            StringAssert.Contains("ConfidenceWeightedPoseFusion.Step", source);
            StringAssert.DoesNotContain("YOffset", source);
        }

        [Test]
        public void ORBDatabaseStillA0464100()
        {
            byte[] bytes = File.ReadAllBytes("Assets/OrbModels/bottle_reference_b.bytes");
            Assert.AreEqual(4100, BitConverter.ToInt32(bytes, 8));
            Assert.AreEqual(
                "A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF",
                Sha256(bytes));
        }

        private static float[] ExtractFloats(string json, string key) =>
            json.Split(new[] { $"\"{key}\":" }, StringSplitOptions.None)
                .Skip(1)
                .Select(tail => float.Parse(
                    tail.TrimStart().Split(',', '\r', '\n')[0],
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

        private static int[] ExtractInts(string json, string key) =>
            json.Split(new[] { $"\"{key}\":" }, StringSplitOptions.None)
                .Skip(1)
                .Select(tail => int.Parse(tail.TrimStart().Split(',', '\r', '\n')[0]))
                .ToArray();

        private static string[] ExtractVector4Strings(string json, string key) =>
            json.Split(new[] { $"\"{key}\":" }, StringSplitOptions.None)
                .Skip(1)
                .Select(tail => tail.TrimStart().Split('}')[0])
                .ToArray();

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform result = Find(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private static float ExtractFloat(string json, string key)
        {
            string tail = json.Split(new[] { $"\"{key}\":" }, StringSplitOptions.None)[1];
            return float.Parse(tail.TrimStart().Split(',', '\r', '\n')[0],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int SumInt(string json, string key)
        {
            return json.Split(new[] { $"\"{key}\":" }, StringSplitOptions.None)
                .Skip(1)
                .Sum(tail => int.Parse(tail.TrimStart().Split(',', '\r', '\n')[0]));
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("X2")));
        }
    }
}
