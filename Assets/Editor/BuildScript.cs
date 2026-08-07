using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ScrapSiege.EditorTools
{
    /// <summary>
    /// One Android build entry point, callable two ways:
    ///
    ///   1. From the live Editor (Unity MCP / menu item) - BuildAndroidFromEditor().
    ///      This is the preferred path in this project because the Editor is normally already
    ///      open, and Unity batchmode cannot open a project whose Library/ is locked by a
    ///      running Editor instance.
    ///   2. From batchmode CI - `Unity.exe -quit -batchmode -projectPath . -executeMethod
    ///      ScrapSiege.EditorTools.BuildScript.BuildAndroidBatch`. Only usable with the Editor
    ///      CLOSED.
    ///
    /// Both produce build/ScrapSiege.apk. Development builds are auto-signed by Unity with the
    /// Android debug keystore, so no keystore configuration is needed for Internal-Testing-style
    /// installs; a Play Store upload would need real signing set in Player Settings.
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDirectory = "build";
        private const string ApkName = "ScrapSiege.apk";

        public static string OutputPath => Path.Combine(OutputDirectory, ApkName);

        [MenuItem("Scrap Siege/Build Android APK (Development)")]
        public static void BuildAndroidFromEditorMenu() => BuildAndroidFromEditor(development: true);

        /// <summary>
        /// Runs a build inside the already-open Editor. Returns a short human-readable summary
        /// rather than throwing, so the MCP caller always gets something actionable back.
        /// </summary>
        public static string BuildAndroidFromEditor(bool development = true)
        {
            try
            {
                BuildReport report = RunBuild(development);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    // summary.totalSize counts every build artifact (symbols, il2cpp output, the
                    // whole build/ tree), which reported 1.6 GB for a 97 MB APK. Stat the actual
                    // file instead - the APK size is what matters for an adb install.
                    string apkSize = File.Exists(OutputPath)
                        ? $"{new FileInfo(OutputPath).Length / (1024 * 1024)} MB APK"
                        : "APK MISSING";

                    return $"SUCCESS  {OutputPath}  ({apkSize}, {summary.totalTime.TotalSeconds:0} s, {summary.totalErrors} errors, {summary.totalWarnings} warnings)";
                }

                string firstErrors = FirstErrorMessages(report);
                return $"FAILED  result={summary.result}  errors={summary.totalErrors}\n{firstErrors}";
            }
            catch (Exception ex)
            {
                return $"EXCEPTION  {ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>Batchmode entry point. Sets the process exit code so CI/scripts can branch on it.</summary>
        public static void BuildAndroidBatch()
        {
            bool development = Environment.GetCommandLineArgs().Contains("-scrapReleaseBuild") == false;

            try
            {
                BuildReport report = RunBuild(development);

                if (report.summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[BuildScript] SUCCESS -> {OutputPath}");
                    EditorApplication.Exit(0);
                    return;
                }

                Debug.LogError($"[BuildScript] FAILED result={report.summary.result}\n{FirstErrorMessages(report)}");
                EditorApplication.Exit(1);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildScript] EXCEPTION {ex}");
                EditorApplication.Exit(1);
            }
        }

        private static BuildReport RunBuild(bool development)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                throw new InvalidOperationException(
                    $"Active build target is {EditorUserBuildSettings.activeBuildTarget}, not Android. " +
                    "Switch via File > Build Profiles (a target switch reimports assets and can take several minutes, " +
                    "so this is deliberately not done automatically).");
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes in Build Settings - the APK would have nothing to run.");

            Directory.CreateDirectory(OutputDirectory);

            // APK, not AAB: this build is installed directly over adb for on-device testing.
            EditorUserBuildSettings.buildAppBundle = false;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    // Development + script debugging keeps full logging (and readable stack traces)
                    // in logcat, which is how essentially every on-device bug in this project has
                    // actually been diagnosed.
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            Debug.Log($"[BuildScript] Building {(development ? "development" : "release")} APK from {scenes.Length} scene(s) -> {OutputPath}");
            return BuildPipeline.BuildPlayer(options);
        }

        /// <summary>
        /// Pulls the actual error lines out of the build report. summary.totalErrors alone only
        /// gives a count, which is never enough to fix anything.
        /// </summary>
        private static string FirstErrorMessages(BuildReport report, int max = 10)
        {
            var errors = report.steps
                .SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Exception)
                .Select(message => message.content.Trim())
                .Take(max)
                .ToArray();

            return errors.Length > 0
                ? string.Join("\n", errors)
                : "(no error messages captured in the build report - check the Editor console)";
        }
    }
}
