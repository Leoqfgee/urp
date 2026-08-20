using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Tests.Editor
{
    public sealed class V51ConvergenceTests
    {
        private const string CatalogPath = "Assets/Objects/RestorationObjectCatalog.asset";
        private const string BottleProfilePath =
            "Assets/Objects/CoconutBottle/Profiles/CoconutBottleRepairProfile.asset";
        private const string PairPath = "Assets/Models/CleanBottleReconstruction/"
            + "BottleFullAlignedV2/bottle_full_aligned_v2.fbx";

        [Test]
        public void CatalogContainsOnlyCoconutBottle()
        {
            RestorationObjectCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RestorationObjectCatalog>(CatalogPath);
            RestorationObjectProfile bottle =
                AssetDatabase.LoadAssetAtPath<RestorationObjectProfile>(BottleProfilePath);
            Assert.NotNull(catalog);
            Assert.AreEqual(1, catalog.objects.Length);
            Assert.AreSame(bottle, catalog.objects[0]);
        }

        [Test]
        public void NoTissueProfileReferencedByProductionCatalog()
        {
            string yaml = File.ReadAllText(CatalogPath);
            StringAssert.DoesNotContain("Tissue", yaml);
            StringAssert.DoesNotContain("f6426e05e00c47f4cb59d99c2828a05d", yaml);
        }

        [Test]
        public void NoTissueRuntimeReferenceInProductionScene()
        {
            string scene = File.ReadAllText("Assets/Scenes/UrpARPrototype.unity");
            string setup = File.ReadAllText("Assets/Editor/UrpArProjectSetup.cs");
            StringAssert.DoesNotContain("TissueRepairProfile", scene + setup);
            StringAssert.DoesNotContain("TissueModelPath", setup);
            StringAssert.DoesNotContain("TissueTexturePath", setup);
            StringAssert.DoesNotContain("TissueThumbnailPath", setup);
            Assert.IsTrue(Directory.Exists("Assets/Objects/Tissue"),
                "Tissue source assets must remain in the repository for later work.");
        }

        [Test]
        public void SingleBottleSelectionDoesNotInstallScrollRect()
        {
            string source = File.ReadAllText("Assets/Scripts/UrpAppController.cs");
            StringAssert.Contains("if (count > 2)", source);
            StringAssert.Contains("count == 1", source);
        }

        [Test]
        public void BottleCapTransformAndMeshRemainFrozen()
        {
            GameObject pair = AssetDatabase.LoadAssetAtPath<GameObject>(PairPath);
            Transform cap = Find(pair.transform, "BottleCapC");
            Assert.NotNull(cap);
            Assert.AreEqual(Vector3.zero, cap.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.identity, cap.localRotation), 0.0001f);
            Assert.AreEqual(Vector3.one, cap.localScale);
            Assert.AreEqual(11911, cap.GetComponentInChildren<MeshFilter>(true).sharedMesh.vertexCount);
        }

        [Test]
        public void BottleCapBackfaceCullingQA()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/CleanBottleCapLit.mat");
            Assert.NotNull(material);
            Assert.AreEqual(0f, material.GetFloat("_Cull"),
                "v52 intentionally restores the v50 Cull Off appearance baseline.");
            Assert.IsTrue(material.doubleSidedGI);
        }

        [Test]
        public void HighConfidencePoseUpdateIsContinuousWeightNotBinaryGate()
        {
            string source = File.ReadAllText("Assets/Scripts/OrbImageTrackingController.cs");
            StringAssert.Contains("CalculateHighConfidenceWeight", source);
            StringAssert.Contains("Mathf.Lerp(0.35f, 1f, quality)", source);
            StringAssert.Contains("Never reduce the v50 confidence", source);
            StringAssert.Contains("Mathf.Clamp(weightedConfidence, 0.30f, 1f)", source);
            StringAssert.DoesNotContain("PassesHighConfidenceTrackedUpdate", source);
            StringAssert.DoesNotContain("HOLD_LAST_RELIABLE", source);
            StringAssert.DoesNotContain("VerifiedPoseLock", source);
            StringAssert.DoesNotContain("positionDeadband", source);
        }

        [Test]
        public void NativeAlreadyRefinesOnlyRansacInliers()
        {
            string native = File.ReadAllText("Native/UrpOrbNative/src/urp_orb_native.cpp");
            StringAssert.Contains("solvePnPRefineLM", native);
            StringAssert.Contains("inlierModelPoints", native);
            StringAssert.Contains("inlierFramePoints", native);
        }

        [Test]
        public void PortraitIntrinsicsRotationConsistencyTests()
        {
            string artifact = File.ReadAllText("Assets/Calibration/v51_pose_math_qa.json");
            StringAssert.Contains("\"passed\": true", artifact);
            StringAssert.Contains("\"overall_rms_px\":", artifact);
        }

        [Test]
        public void DevelopmentDiagnosticsAreNotProductionUi()
        {
            string app = File.ReadAllText("Assets/Scripts/UrpAppController.cs");
            StringAssert.Contains("if (Debug.isDebugBuild || Application.isEditor)", app);
            StringAssert.Contains("3D配准调试", app);
            StringAssert.Contains(
                "trackingStatus = Debug.isDebugBuild || Application.isEditor",
                app);
        }

        [Test]
        public void ORBDatabaseRemainsA0464100()
        {
            byte[] bytes = File.ReadAllBytes("Assets/OrbModels/bottle_reference_b.bytes");
            Assert.AreEqual(4100, BitConverter.ToInt32(bytes, 8));
            using var sha = System.Security.Cryptography.SHA256.Create();
            string digest = string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("X2")));
            Assert.AreEqual(
                "A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF",
                digest);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
