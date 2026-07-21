using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Urp.ArDemo.Generated;

namespace Urp.ArDemo.Editor
{
    public static class BuildIdentityGenerator
    {
        public const string ResourcePath = "Assets/Resources/BuildIdentity.json";
        public const string StreamingPath = "Assets/StreamingAssets/build_identity.json";
        private const string NativeSourcePath = "Native/UrpOrbNative/src/urp_orb_native.cpp";
        private const string NativePluginPath =
            "Assets/Plugins/Android/arm64-v8a/libUrpOrbNative.so";
        private const string OrbPath = "Assets/OrbModels/bottle_reference_b.bytes";
        private const string CalibrationPath =
            "Assets/Calibration/CoconutBottleRepairCalibration.asset";

        public static BuildIdentityData Generate()
        {
            Directory.CreateDirectory("Assets/Resources");
            Directory.CreateDirectory("Assets/StreamingAssets");
            string nativeVersion = ReadNativeVersion();
            byte[] nativeBytes = File.ReadAllBytes(NativePluginPath);
            byte[] versionBytes = Encoding.ASCII.GetBytes(nativeVersion);
            if (!Contains(nativeBytes, versionBytes))
                throw new InvalidOperationException(
                    "Native C++ source version is absent from libUrpOrbNative.so; rebuild the plugin.");
            BuildIdentityData data = new BuildIdentityData
            {
                gitCommit = Git("rev-parse HEAD"),
                gitBranch = Git("branch --show-current"),
                gitDirty = IsDirtyBeforeGeneration(),
                builtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                unityVersion = Application.unityVersion,
                trackingBuildVersion = BuildIdentity.TrackingBuildVersion,
                nativeBuildVersion = nativeVersion,
                orbDatabaseVersion = BuildIdentity.OrbDatabaseVersion,
                calibrationVersion = BuildIdentity.CalibrationVersion,
                orbDatabaseSha256 = Sha256(OrbPath),
                calibrationSha256 = Sha256(CalibrationPath)
            };
            string json = JsonUtility.ToJson(data, true) + Environment.NewLine;
            File.WriteAllText(ResourcePath, json, new UTF8Encoding(false));
            File.WriteAllText(StreamingPath, json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(StreamingPath, ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Debug.Log($"[BuildIdentity] Generated\n{data.ShortText}");
            return data;
        }

        public static BuildIdentityData VerifyApk(string apkPath, BuildIdentityData expected)
        {
            using ZipArchive archive = ZipFile.OpenRead(apkPath);
            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(value =>
                value.FullName.EndsWith("build_identity.json",
                    StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new InvalidOperationException("APK does not contain build_identity.json.");
            using StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8);
            BuildIdentityData actual = JsonUtility.FromJson<BuildIdentityData>(reader.ReadToEnd());
            if (actual == null
                || actual.gitCommit != expected.gitCommit
                || actual.builtUtc != expected.builtUtc
                || actual.nativeBuildVersion != expected.nativeBuildVersion)
                throw new InvalidOperationException("APK BuildIdentity does not match this build invocation.");
            UnityEngine.Debug.Log($"[BuildIdentity] APK verified\n{actual.ShortText}");
            return actual;
        }

        public static string VerifyNativePluginInApk(string apkPath)
        {
            using ZipArchive archive = ZipFile.OpenRead(apkPath);
            ZipArchiveEntry entry = archive.GetEntry("lib/arm64-v8a/libUrpOrbNative.so");
            if (entry == null)
                throw new InvalidOperationException("APK is missing arm64-v8a/libUrpOrbNative.so.");
            string packaged;
            using (SHA256 sha = SHA256.Create())
            using (Stream stream = entry.Open())
                packaged = string.Concat(sha.ComputeHash(stream)
                    .Select(value => value.ToString("X2")));
            string local = Sha256(NativePluginPath);
            if (!string.Equals(packaged, local, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"APK Native SHA mismatch: packaged={packaged}, local={local}");
            UnityEngine.Debug.Log($"[BuildIdentity] APK Native SHA256: {packaged}");
            return packaged;
        }

        public static string Sha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("X2")));
        }

        private static bool IsDirtyBeforeGeneration()
        {
            string status = Git("status --porcelain --untracked-files=normal");
            return status.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => !line.Contains("Assets/Resources/BuildIdentity.json")
                    && !line.Contains("Assets/StreamingAssets/build_identity.json"));
        }

        private static string ReadNativeVersion()
        {
            string source = File.ReadAllText(NativeSourcePath);
            const string marker = "constexpr char kBuildVersion[] = \"";
            int start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return "unknown";
            start += marker.Length;
            int end = source.IndexOf('"', start);
            return end > start ? source.Substring(start, end - start) : "unknown";
        }

        private static string Git(string arguments)
        {
            using Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {arguments} failed: {error}");
            return output;
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length) return false;
            for (int start = 0; start <= haystack.Length - needle.Length; start++)
            {
                int offset = 0;
                while (offset < needle.Length && haystack[start + offset] == needle[offset])
                    offset++;
                if (offset == needle.Length) return true;
            }
            return false;
        }
    }
}
