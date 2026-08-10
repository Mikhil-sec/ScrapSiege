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
    /// Development builds produce build/ScrapSiege.apk, auto-signed by Unity with the Android
    /// debug keystore - installable directly over adb, no keystore configuration needed.
    /// Release builds produce build/ScrapSiege.aab: Google Play rejects bare APK uploads for any
    /// app first published after August 2021, so the release path must be an App Bundle, and it
    /// requires the real upload keystore configured in Player Settings (see RunBuild's guard).
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDirectory = "build";
        private const string ApkName = "ScrapSiege.apk";
        private const string AabName = "ScrapSiege.aab";

        public static string OutputPathFor(bool development) =>
            Path.Combine(OutputDirectory, development ? ApkName : AabName);

        public static string OutputPath => OutputPathFor(development: true);

        [MenuItem("Scrap Siege/Build Android APK (Development)")]
        public static void BuildAndroidFromEditorMenu() => BuildAndroidFromEditor(development: true);

        /// <summary>
        /// The only path in this project that should ever produce a Play-uploadable artifact:
        /// non-debuggable, and it refuses to run at all unless a real upload keystore is
        /// configured, so it can never silently fall back to the Android debug certificate.
        /// </summary>
        [MenuItem("Scrap Siege/Build Android APK (RELEASE - for Play Store)")]
        public static void BuildAndroidReleaseFromEditorMenu()
        {
            string result = BuildAndroidFromEditor(development: false);
            Debug.Log($"[BuildScript] {result}");
        }

        /// <summary>
        /// Re-applies the upload keystore passwords, which Unity drops on every Editor restart.
        /// Reads them from environment variables rather than any file in this repo - the repo is
        /// public, and a signing password must never be committable (see SECURITY.md).
        /// Only needed before a RELEASE build; development builds no longer touch the keystore.
        /// </summary>
        [MenuItem("Scrap Siege/Apply Release Keystore Passwords (from environment)")]
        public static void ApplyKeystorePasswordsFromEnvironment()
        {
            string keystorePass = Environment.GetEnvironmentVariable("SCRAPSIEGE_KEYSTORE_PASS");
            if (string.IsNullOrEmpty(keystorePass))
            {
                Debug.LogError(
                    "[BuildScript] SCRAPSIEGE_KEYSTORE_PASS is not set. Set it as a Windows user " +
                    "environment variable (then restart Unity Hub so the Editor inherits it) and run " +
                    "this menu item again. Never put the password in a file inside this repo.");
                return;
            }

            // The alias password is usually the same as the store password for a keytool-generated
            // key, so default to it rather than forcing a second variable to be set.
            string aliasPass = Environment.GetEnvironmentVariable("SCRAPSIEGE_KEYALIAS_PASS");
            if (string.IsNullOrEmpty(aliasPass)) aliasPass = keystorePass;

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasPass = aliasPass;

            Debug.Log(
                $"[BuildScript] Keystore passwords applied for this Editor session " +
                $"(keystore={PlayerSettings.Android.keystoreName}, alias={PlayerSettings.Android.keyaliasName}). " +
                "They will be lost again on the next Editor restart - that is Unity's behaviour, not a bug.");
        }

        /// <summary>
        /// Runs a build inside the already-open Editor. Returns a short human-readable summary
        /// rather than throwing, so the MCP caller always gets something actionable back.
        /// </summary>
        public static string BuildAndroidFromEditor(bool development = true)
        {
            string outputPath = OutputPathFor(development);
            try
            {
                BuildReport report = RunBuild(development);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    // summary.totalSize counts every build artifact (symbols, il2cpp output, the
                    // whole build/ tree), which reported 1.6 GB for a 97 MB APK. Stat the actual
                    // file instead - the APK size is what matters for an adb install.
                    string label = development ? "APK" : "AAB";
                    string apkSize = File.Exists(outputPath)
                        ? $"{new FileInfo(outputPath).Length / (1024 * 1024)} MB {label}"
                        : $"{label} MISSING";

                    return $"SUCCESS  {outputPath}  ({apkSize}, {summary.totalTime.TotalSeconds:0} s, {summary.totalErrors} errors, {summary.totalWarnings} warnings)";
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
                    Debug.Log($"[BuildScript] SUCCESS -> {OutputPathFor(development)}");
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

            if (!development && !PlayerSettings.Android.useCustomKeystore)
            {
                throw new InvalidOperationException(
                    "Refusing to produce a release build without a real upload keystore configured " +
                    "(PlayerSettings.Android.useCustomKeystore is false). Without one, Unity silently " +
                    "signs with the Android debug certificate, which Play rejects and which defeats the " +
                    "point of a release build. Set the keystore in Player Settings > Publishing Settings first.");
            }

            Directory.CreateDirectory(OutputDirectory);

            // Development -> APK, installed directly over adb for on-device testing.
            // Release -> AAB, required for any Play Store upload (see class doc).
            EditorUserBuildSettings.buildAppBundle = !development;
            string outputPath = OutputPathFor(development);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    // Development + script debugging keeps full logging (and readable stack traces)
                    // in logcat, which is how essentially every on-device bug in this project has
                    // actually been diagnosed.
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            Debug.Log($"[BuildScript] Building {(development ? "development APK" : "release AAB")} from {scenes.Length} scene(s) -> {outputPath}");

            // `androidUseCustomKeystore` is persisted in ProjectSettings.asset, but Unity
            // deliberately never persists the keystore PASSWORDS - they live in memory only and are
            // gone after every Editor restart. That combination means a plain development build,
            // which has no business touching the upload key at all, fails asking for a password it
            // cannot have. So dev builds explicitly opt out of the custom keystore and fall back to
            // the Android debug certificate (which is all adb install needs), then restore the
            // setting so ProjectSettings.asset is left byte-identical for the next release build.
            if (!development)
                return BuildPipeline.BuildPlayer(options);

            bool restoreCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            try
            {
                PlayerSettings.Android.useCustomKeystore = false;
                return BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.Android.useCustomKeystore = restoreCustomKeystore;

                // Flushed explicitly, not left dirty in memory. Unity writes ProjectSettings.asset
                // lazily, so without this the file can sit on disk with the temporary
                // useCustomKeystore=false from mid-build until something else happens to save -
                // which shows up as a spurious diff in a tracked file, and as a release build that
                // trips its own "no keystore configured" guard after an Editor crash.
                AssetDatabase.SaveAssets();
            }
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
