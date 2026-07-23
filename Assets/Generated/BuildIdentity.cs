using System;
using UnityEngine;

namespace Urp.ArDemo.Generated
{
    [Serializable]
    public sealed class BuildIdentityData
    {
        public string gitCommit;
        public string gitBranch;
        public bool gitDirty;
        public string builtUtc;
        public string unityVersion;
        public string trackingBuildVersion;
        public string nativeBuildVersion;
        public string orbDatabaseVersion;
        public string calibrationVersion;
        public string orbDatabaseSha256;
        public string calibrationSha256;

        public string ShortText =>
            $"Git: {ShortCommit}{(gitDirty ? "-dirty" : string.Empty)}\n"
            + $"Branch: {gitBranch}\n"
            + $"Built: {builtUtc}\n"
            + $"Unity: {unityVersion}\n"
            + $"Tracking: {trackingBuildVersion}\n"
            + $"Native: {nativeBuildVersion}\n"
            + $"ORB DB: {orbDatabaseVersion}\n"
            + $"Calibration: {calibrationVersion}";

        private string ShortCommit => string.IsNullOrEmpty(gitCommit)
            ? "unknown"
            : gitCommit.Substring(0, Mathf.Min(12, gitCommit.Length));
    }

    /// <summary>
    /// Stable runtime facade for the generated Resources/BuildIdentity.json.
    /// Keeping the values in JSON avoids the circular problem where embedding
    /// a commit hash in compiled C# would itself create another commit.
    /// </summary>
    public static class BuildIdentity
    {
        public const string TrackingBuildVersion = "orb-tracking-v17-guided-prealignment-rigid-bc";
        public const string CalibrationVersion = "coconut-bc-rigid-registration-v7";
        public const string OrbDatabaseVersion = "bottle-full-aligned-v2-reference-b-rendered-v1";

        private static BuildIdentityData cached;

        public static BuildIdentityData Current
        {
            get
            {
                if (cached != null)
                {
                    return cached;
                }

                TextAsset asset = Resources.Load<TextAsset>("BuildIdentity");
                if (asset != null)
                {
                    cached = JsonUtility.FromJson<BuildIdentityData>(asset.text);
                }

                cached ??= new BuildIdentityData
                {
                    gitCommit = "not-generated",
                    gitBranch = "unknown",
                    builtUtc = "not-generated",
                    unityVersion = Application.unityVersion,
                    trackingBuildVersion = TrackingBuildVersion,
                    nativeBuildVersion = "unknown",
                    orbDatabaseVersion = OrbDatabaseVersion,
                    calibrationVersion = CalibrationVersion
                };
                return cached;
            }
        }
    }
}
