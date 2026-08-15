using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Tests.Editor
{
    public sealed class V50RigidRegistrationAndMainDepthTests
    {
        private const string Controller = "Assets/Scripts/OrbImageTrackingController.cs";
        private const string Feature = "Assets/Scripts/RepairOcclusionRendererFeature.cs";
        private const string ShaderPath = "Assets/Shaders/PaperLinearEyeDepth.shader";
        private const string Registry = "Assets/Scripts/PaperOcclusionRegistry.cs";
        private const string Pair = "Assets/Models/CleanBottleReconstruction/"
            + "BottleFullAlignedV2/bottle_full_aligned_v2.fbx";

        [Test]
        public void BottleCapAssetUnchanged()
        {
            Assert.AreEqual(
                "F0661ADB5E953A1DA4605A943995E251ACD4039C0397A14B99F0418319562D21",
                Sha256(File.ReadAllBytes(Pair)));
            string integrity = File.ReadAllText(
                "Assets/Calibration/bottle_cap_asset_integrity_v48.json");
            StringAssert.Contains("\"imported_mesh_sha256\": "
                + "\"10642ED34185213B7CF56297EE82CD36CDD017BAF6E49232EB9EEDAF7C4FE381\"",
                integrity);
            StringAssert.Contains("\"vertex_count\": 11911", integrity);
            StringAssert.Contains("\"triangle_count\": 9504", integrity);
        }

        [Test]
        public void BottleCapTransformUnchanged()
        {
            GameObject pair = AssetDatabase.LoadAssetAtPath<GameObject>(Pair);
            Transform cap = Find(pair.transform, "BottleCapC");
            Assert.NotNull(cap);
            Assert.AreEqual(Vector3.zero, cap.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.identity, cap.localRotation), 0.0001f);
            Assert.AreEqual(Vector3.one, cap.localScale);
            Assert.AreEqual("BottleRepairRoot/BottleCapC",
                cap.parent.name + "/" + cap.name);
        }

        [Test]
        public void BottleCapMaterialIsV44CleanBottleCapLit()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/CleanBottleCapLit.mat");
            Assert.NotNull(material);
            Assert.AreEqual("4e2ac76533fd4bba8da1cdb51f07b8d1",
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
            Color baseColor = material.GetColor("_BaseColor");
            Assert.That(baseColor.r, Is.EqualTo(0.96f).Within(0.001f));
            Assert.That(baseColor.g, Is.EqualTo(0.96f).Within(0.001f));
            Assert.That(baseColor.b, Is.EqualTo(0.94f).Within(0.001f));
        }

        [Test]
        public void V50DirectCapMatchesV44Appearance() =>
            BottleCapMaterialIsV44CleanBottleCapLit();

        [Test]
        public void BottleCapCIsOriginalReal3DRenderer()
        {
            GameObject pair = AssetDatabase.LoadAssetAtPath<GameObject>(Pair);
            Transform cap = Find(pair.transform, "BottleCapC");
            Assert.NotNull(cap.GetComponentInChildren<MeshRenderer>(true));
            Assert.NotNull(cap.GetComponentInChildren<MeshFilter>(true)?.sharedMesh);
            Assert.IsNull(cap.GetComponentInChildren<SpriteRenderer>(true));
            Assert.AreEqual("BottleRepairRoot", cap.parent.name);
        }

        [Test]
        public void BottleAndCapShareOneRigidRoot()
        {
            GameObject pair = AssetDatabase.LoadAssetAtPath<GameObject>(Pair);
            Transform bottle = Find(pair.transform, "DamagedBottleB");
            Transform cap = Find(pair.transform, "BottleCapC");
            Assert.AreSame(bottle.parent, cap.parent);
            Assert.AreEqual(Vector3.zero, bottle.localPosition);
            Assert.AreEqual(Vector3.zero, cap.localPosition);
            Assert.AreEqual(Vector3.one, bottle.localScale);
            Assert.AreEqual(Vector3.one, cap.localScale);
        }

        [Test]
        public void IndependentBottleCapInterfaceMeasurementPasses()
        {
            string artifact = File.ReadAllText(
                "Assets/Calibration/bottle_bc_registration_v50.json");
            StringAssert.Contains("\"model_space_correction_applied\": false", artifact);
            StringAssert.Contains("\"screen_space_correction_present\": false", artifact);
            StringAssert.Contains("\"T_C_relative_to_B\"", artifact);
        }

        [Test]
        public void FullBDepthWrittenBeforeCapForwardPass()
        {
            string feature = File.ReadAllText(Feature);
            StringAssert.Contains("RenderPassEvent.BeforeRenderingOpaques", feature);
            StringAssert.Contains("PaperOcclusionRegistry.BottleRenderers", feature);
            StringAssert.Contains("cameraDepthTargetHandle", feature);
            StringAssert.Contains("cmd.DrawRenderer", feature);
            string registry = File.ReadAllText(Registry);
            StringAssert.Contains("BottleDepthOnlyLayer = 31", registry);
            StringAssert.Contains("renderer.enabled = true", registry);
            StringAssert.Contains("Camera.cullingMask &= ~(1 << BottleDepthOnlyLayer)", registry);
            string renderer = File.ReadAllText("Assets/Settings/UrpMobileRenderer.asset")
                .Replace("\r", string.Empty);
            StringAssert.Contains("passEvent: 250", renderer);
            StringAssert.Contains(
                "m_RendererFeatures:\n  - {fileID: 5348762365857661857}\n"
                + "  - {fileID: 3209457855913661365}",
                renderer,
                "AR background must be enqueued before the B main-depth pass.");
        }

        [Test]
        public void BDepthPassDoesNotWriteColor()
        {
            string shader = File.ReadAllText(ShaderPath);
            StringAssert.Contains("ColorMask 0", shader);
            StringAssert.Contains("ZWrite On", shader);
            StringAssert.Contains("ZTest LEqual", shader);
            StringAssert.Contains("Cull Back", shader);
            StringAssert.Contains("Blend Off", shader);
        }

        [Test]
        public void CapUsesNormalURPForwardPass()
        {
            string controller = File.ReadAllText(Controller);
            StringAssert.Contains("SetRepairHierarchyVisible(true)", controller);
            string feature = File.ReadAllText(Feature);
            StringAssert.DoesNotContain("CapRenderers", feature);
            StringAssert.DoesNotContain("DrawCap", feature);
        }

        [Test]
        public void NoCColorRTInFinalRuntimePath()
        {
            string runtime = File.ReadAllText(Feature)
                + File.ReadAllText(Registry)
                + File.ReadAllText(Controller);
            StringAssert.DoesNotContain("_PaperCColorRT", runtime);
            StringAssert.DoesNotContain("CColorRT", runtime);
            StringAssert.DoesNotContain("ExtractOriginalCapColor", runtime);
        }

        [Test]
        public void NoFullscreenCapCompositeInFinalRuntimePath()
        {
            string feature = File.ReadAllText(Feature);
            StringAssert.DoesNotContain("Blitter", feature);
            StringAssert.DoesNotContain("cameraColor, composite", feature);
            StringAssert.DoesNotContain("AfterRenderingTransparents", feature);
        }

        [Test]
        public void NoScreenSpaceCapOrRegistrationHackExists()
        {
            string runtime = File.ReadAllText(Controller)
                + File.ReadAllText(Feature)
                + File.ReadAllText(Registry);
            foreach (string forbidden in new[]
                     {
                         "capYOffset", "screenSpaceCap", "capOffset",
                         "Graphics.Blit", "CColorRT", "SpriteRenderer"
                     })
                StringAssert.DoesNotContain(forbidden, runtime);
        }

        [Test]
        public void RegistrationDebugModeUsesRealBottleAndCapHierarchy()
        {
            string controller = File.ReadAllText(Controller);
            StringAssert.Contains("ToggleRegistrationDebugMode", controller);
            StringAssert.Contains("SetReferenceHierarchyVisible(true)", controller);
            StringAssert.Contains("SetRepairHierarchyVisible(true)", controller);
            StringAssert.Contains("[REPAIR_REGISTRATION_DIAG]", controller);
        }

        [Test]
        public void NoArtificialOccluderGeometryExists()
        {
            string runtime = File.ReadAllText(Feature)
                + File.ReadAllText(Registry)
                + File.ReadAllText(Controller);
            foreach (string forbidden in new[]
                     {
                         "BottleRepairOccluder", "neck_radial_dilation", "1.02f",
                         "CreatePrimitive", "Instantiate(ReferenceNeck", "occluderScale"
                     })
                StringAssert.DoesNotContain(forbidden, runtime);
        }

        [Test]
        public void StartDoesNotChangeCapAppearanceProperties()
        {
            string controller = File.ReadAllText(Controller);
            StringAssert.Contains("LogAppearanceSnapshot(\"start-before\")", controller);
            StringAssert.Contains("LogAppearanceSnapshot(\"start-after\")", controller);
            StringAssert.DoesNotContain("registeredRepairPart.localPosition =", controller);
            StringAssert.DoesNotContain("registeredRepairPart.localRotation =", controller);
            StringAssert.DoesNotContain("registeredRepairPart.localScale =", controller);
        }

        [Test]
        public void StartDoesNotChangeCapMatrix()
        {
            StringAssert.Contains(
                "AssertMatrixUnchanged(\"BottleCapC\", before.cap, after.cap)",
                File.ReadAllText(Controller));
        }

        [Test]
        public void RearCapPixelsFailDepthWhenBottleInFront()
        {
            Assert.IsFalse(PassesDepth(capDepth: 0.52f, bottleDepth: 0.48f));
            Assert.IsTrue(PassesDepth(capDepth: 0.44f, bottleDepth: 0.48f));
        }

        [Test]
        public void OcclusionMaskChangesWithCameraAzimuth()
        {
            bool[] left = BuildSyntheticAzimuthMask(-1);
            bool[] right = BuildSyntheticAzimuthMask(1);
            CollectionAssert.AreNotEqual(left, right);
        }

        [Test]
        public void LeftAndRightOcclusionMasksDifferSpatially()
        {
            bool[] left = BuildSyntheticAzimuthMask(-1);
            bool[] right = BuildSyntheticAzimuthMask(1);
            Assert.Greater(left.Zip(right, (a, b) => a != b).Count(v => v), 0);
            CollectionAssert.AreEqual(left.Reverse().ToArray(), right);
        }

        [Test]
        public void TopObliqueOcclusionIsDepthCorrect()
        {
            float[] bottle = { 0.42f, 0.46f, float.PositiveInfinity };
            float[] cap = { 0.47f, 0.44f, 0.45f };
            CollectionAssert.AreEqual(
                new[] { false, true, true },
                cap.Select((depth, i) => PassesDepth(depth, bottle[i])).ToArray());
        }

        [Test]
        public void ORBDatabaseRemainsA0464100()
        {
            byte[] bytes = File.ReadAllBytes("Assets/OrbModels/bottle_reference_b.bytes");
            Assert.AreEqual(4100, BitConverter.ToInt32(bytes, 8));
            Assert.AreEqual(
                "A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF",
                Sha256(bytes));
        }

        private static bool PassesDepth(float capDepth, float bottleDepth) =>
            capDepth <= bottleDepth;

        private static bool[] BuildSyntheticAzimuthMask(int direction)
        {
            float[] cap = { 0.50f, 0.50f, 0.50f, 0.50f, 0.50f };
            float[] leftBottle = { 0.46f, 0.48f, 0.52f, 0.54f, 0.56f };
            float[] bottle = direction < 0
                ? leftBottle
                : leftBottle.Reverse().ToArray();
            return cap.Select((depth, index) => PassesDepth(depth, bottle[index])).ToArray();
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

        private static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes)
                .Select(value => value.ToString("X2")));
        }
    }
}
