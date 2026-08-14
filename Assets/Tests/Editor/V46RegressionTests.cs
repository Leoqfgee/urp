using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Tests.Editor
{
    public sealed class V46RegressionTests
    {
        private const string ControllerPath = "Assets/Scripts/OrbImageTrackingController.cs";

        [Test]
        public void V44BaselineTrackingBehaviorRestored()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("ConfidenceWeightedPoseFusion.Step", source);
            StringAssert.DoesNotContain("VerifiedPoseLock", source);
            StringAssert.DoesNotContain("deadband", source.ToLowerInvariant());
        }

        [Test]
        public void NoBottlePreviewBGhostExists()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.DoesNotContain("BottlePreviewB", source);
            StringAssert.DoesNotContain("CreateBottlePreview", source);
            Assert.IsFalse(File.Exists("Assets/Materials/BottlePreAlignmentGhost.mat"));
        }

        [Test]
        public void VerifiedPoseLockCompletelyRemoved()
        {
            Assert.IsFalse(File.Exists("Assets/Scripts/Calibration/VerifiedPoseLock.cs"));
            Assert.IsFalse(File.Exists("Assets/Tests/Editor/VerifiedPoseLockTests.cs"));
            Assert.IsNull(System.Type.GetType("Urp.ArDemo.Calibration.VerifiedPoseLock"));
        }

        [Test]
        public void DebugOverlayDisabledByDefault()
        {
            GameObject go = new GameObject("PoseDiagnosticTest");
            try
            {
                PoseCoordinateDiagnostic diagnostic = go.AddComponent<PoseCoordinateDiagnostic>();
                Assert.IsFalse(diagnostic.DrawPoseDebugOverlays);
                diagnostic.HideAllDebugLines();
                Assert.AreEqual(0, go.GetComponentsInChildren<LineRenderer>(true).Length);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PoseDiagnosticLogsStillAvailable()
        {
            GameObject go = new GameObject("PoseDiagnosticLogTest");
            try
            {
                PoseCoordinateDiagnostic diagnostic = go.AddComponent<PoseCoordinateDiagnostic>();
                Assert.IsTrue(diagnostic.EmitPoseDiagnosticLogs);
                Assert.IsFalse(diagnostic.DrawPoseDebugOverlays);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ProductionBBlackRegionVisualQA()
        {
            string json = File.ReadAllText("Assets/Calibration/black_region_visual_diagnosis.json");
            StringAssert.Contains("CASE_A_RENDER_GEOMETRY_NORMALS_BACKFACE", json);
            StringAssert.Contains("cull_back_corrected", json);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/BottlePhotogrammetryLit.mat");
            Assert.NotNull(material);
            Assert.AreEqual(2f, material.GetFloat("_Cull"));
        }

        [Test]
        public void BottleOccluderContainsOnlyNeckRegion()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("Instantiate(registeredReferenceNeck.gameObject)", source);
            StringAssert.DoesNotContain("Instantiate(registeredReferenceModel.gameObject)", source);
        }

        [Test]
        public void RepairOccluderWritesDepthNoColor()
        {
            string shader = File.ReadAllText("Assets/Shaders/BottleRepairOccluder.shader");
            StringAssert.Contains("ColorMask 0", shader);
            StringAssert.Contains("ZWrite On", shader);
            StringAssert.Contains("ZTest LEqual", shader);
            StringAssert.Contains("Geometry-10", shader);
        }

        [Test]
        public void CapRemainsVisibleWithOccluder()
        {
            string json = File.ReadAllText("Assets/Calibration/cap_occlusion_coverage_v46.json");
            StringAssert.Contains("\"cap_remains_visible_all_views\": true", json);
            Assert.Greater(ExtractMinimumRetainedRatio(json), 0.40f);
        }

        [Test]
        public void ObliqueViewProducesPartialCapOcclusion()
        {
            string json = File.ReadAllText("Assets/Calibration/cap_occlusion_coverage_v46.json");
            StringAssert.Contains("\"oblique_views_have_partial_occlusion\": true", json);
            Assert.Greater(json.Split(new[] { "\"occluded_pixel_ratio\":" },
                System.StringSplitOptions.None).Length, 4);
        }

        [Test]
        public void FullBodyBNeverWritesRepairDepth()
        {
            string source = File.ReadAllText(ControllerPath);
            MethodInfo method = typeof(OrbImageTrackingController).GetMethod(
                "ShowRepairPresentation",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method);
            StringAssert.Contains("SetReferenceHierarchyVisible(false)", source);
            StringAssert.Contains("SetRepairOccluderVisible(true)", source);
            StringAssert.Contains("registeredReferenceNeck.gameObject", source);
        }

        [Test]
        public void StartDoesNotChangeRigidPose()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("RigidPoseSnapshot before = CaptureRigidPoseSnapshot()", source);
            StringAssert.Contains("AssertStartPoseUnchanged(before, after)", source);
            StringAssert.DoesNotContain("trackedObjectPoseRoot.position =", ExtractStartMethod(source));
        }

        [Test]
        public void StartDoesNotChangeCapMatrix()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("AssertMatrixUnchanged(\"BottleCapC\", before.cap, after.cap)", source);
            StringAssert.DoesNotContain("registeredRepairPart.localPosition", source);
            StringAssert.DoesNotContain("registeredRepairPart.localRotation", source);
        }

        [Test]
        public void ORBDatabaseRemainsA0464100()
        {
            byte[] bytes = File.ReadAllBytes("Assets/OrbModels/bottle_reference_b.bytes");
            Assert.AreEqual(4100, System.BitConverter.ToInt32(bytes, 8));
            using SHA256 sha = SHA256.Create();
            string hash = string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("X2")));
            Assert.AreEqual(
                "A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF",
                hash);
        }

        private static string ExtractStartMethod(string source)
        {
            int start = source.IndexOf("public void StartRecognition()",
                System.StringComparison.Ordinal);
            int end = source.IndexOf("public void ResetTracking()",
                start,
                System.StringComparison.Ordinal);
            return source.Substring(start, end - start);
        }

        private static float ExtractMinimumRetainedRatio(string json)
        {
            string[] parts = json.Split(new[] { "\"retained_pixel_ratio\":" },
                System.StringSplitOptions.None);
            return parts.Skip(1)
                .Select(part => float.Parse(
                    part.TrimStart().Split(',', '\r', '\n')[0],
                    System.Globalization.CultureInfo.InvariantCulture))
                .Min();
        }
    }
}
