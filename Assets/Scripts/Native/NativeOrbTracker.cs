using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Urp.ArDemo.Native
{
    internal sealed class NativeOrbTracker : IDisposable
    {
        private const string DllName = "UrpOrbNative";

        private readonly int handle;
        private bool disposed;

        public NativeOrbTracker(int featureCount, float ratioTest, int minGoodMatches, int maxFrameWidth)
        {
            handle = urp_orb_create(featureCount, ratioTest, minGoodMatches, maxFrameWidth);
        }

        public bool IsValid => handle != 0 && !disposed;

        public static string BuildVersion
        {
            get
            {
                try
                {
                    IntPtr value = urp_orb_get_build_version();
                    return value == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(value);
                }
                catch
                {
                    return "unavailable";
                }
            }
        }

        public unsafe bool SetModel(TextAsset model)
        {
            if (!IsValid || model == null || model.bytes == null || model.bytes.Length == 0)
            {
                return false;
            }

            byte[] bytes = model.bytes;
            fixed (byte* ptr = bytes)
            {
                return urp_orb_set_model(handle, ptr, bytes.Length) != 0;
            }
        }

        public bool SetRepairAnchor(Vector3 anchor)
        {
            return IsValid && urp_orb_set_repair_anchor(handle, anchor.x, anchor.y, anchor.z) != 0;
        }

        public unsafe bool Track(Texture2D texture, CameraIntrinsics intrinsics, out NativeOrbResult result)
        {
            result = default;
            if (!IsValid || texture == null)
            {
                return false;
            }

            byte[] rgba = GetRgbaBytes(texture);
            return Track(rgba, texture.width, texture.height, intrinsics, 0, out result);
        }

        public unsafe bool Track(
            byte[] rgba,
            int width,
            int height,
            CameraIntrinsics intrinsics,
            int rotationClockwise,
            out NativeOrbResult result)
        {
            result = default;
            if (!IsValid || rgba == null || rgba.Length != width * height * 4)
            {
                return false;
            }

            fixed (byte* ptr = rgba)
            {
                return urp_orb_track(
                    handle,
                    ptr,
                    width,
                    height,
                    intrinsics.FocalLengthX,
                    intrinsics.FocalLengthY,
                    intrinsics.PrincipalPointX,
                    intrinsics.PrincipalPointY,
                    rotationClockwise,
                    out result) != 0;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (handle != 0)
            {
                urp_orb_destroy(handle);
            }
        }

        [DllImport(DllName)]
        private static extern int urp_orb_create(int featureCount, float ratio, int minMatches, int maxWidth);

        [DllImport(DllName)]
        private static extern IntPtr urp_orb_get_build_version();

        [DllImport(DllName)]
        private static extern void urp_orb_destroy(int handle);

        [DllImport(DllName)]
        private static extern unsafe int urp_orb_set_model(int handle, byte* data, int length);

        [DllImport(DllName)]
        private static extern int urp_orb_set_repair_anchor(int handle, float x, float y, float z);

        [DllImport(DllName)]
        private static extern unsafe int urp_orb_track(
            int handle,
            byte* rgba,
            int width,
            int height,
            float fx,
            float fy,
            float cx,
            float cy,
            int rotationClockwise,
            out NativeOrbResult result);

        internal static byte[] GetRgbaBytes(Texture2D texture)
        {
            int expectedLength = texture.width * texture.height * 4;
            if (texture.format == TextureFormat.RGBA32)
            {
                byte[] raw = texture.GetRawTextureData<byte>().ToArray();
                if (raw.Length == expectedLength)
                {
                    return raw;
                }
            }

            Color32[] pixels = texture.GetPixels32();
            byte[] rgba = new byte[expectedLength];
            for (int i = 0; i < pixels.Length; i++)
            {
                int offset = i * 4;
                rgba[offset] = pixels[i].r;
                rgba[offset + 1] = pixels[i].g;
                rgba[offset + 2] = pixels[i].b;
                rgba[offset + 3] = pixels[i].a;
            }

            return rgba;
        }
    }

    internal readonly struct CameraIntrinsics
    {
        public CameraIntrinsics(float focalLengthX, float focalLengthY, float principalPointX, float principalPointY)
        {
            FocalLengthX = focalLengthX;
            FocalLengthY = focalLengthY;
            PrincipalPointX = principalPointX;
            PrincipalPointY = principalPointY;
        }

        public float FocalLengthX { get; }
        public float FocalLengthY { get; }
        public float PrincipalPointX { get; }
        public float PrincipalPointY { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeOrbResult
    {
        public int tracked;
        public int goodMatches;
        public float centerX01;
        public float centerY01;
        public float relativeWidth;
        public float topLeftX01;
        public float topLeftY01;
        public float topRightX01;
        public float topRightY01;
        public float bottomRightX01;
        public float bottomRightY01;
        public float bottomLeftX01;
        public float bottomLeftY01;
        public int poseValid;
        public int poseInliers;
        public float tvecX;
        public float tvecY;
        public float tvecZ;
        public float r00;
        public float r01;
        public float r02;
        public float r10;
        public float r11;
        public float r12;
        public float r20;
        public float r21;
        public float r22;
        public float reprojectionError;
        public float anchorX01;
        public float anchorY01;
        public float anchorDepth;
        public int anchorVisible;
        public float localLuminance;
        public float inlierRatio;
        public float coverageX;
        public float coverageY;
        public float modelSpread;
        public float processingMilliseconds;
    }
}
