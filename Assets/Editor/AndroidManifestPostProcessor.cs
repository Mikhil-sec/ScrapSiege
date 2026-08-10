using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace ScrapSiege.EditorTools
{
    /// <summary>
    /// Adds <c>HIGH_SAMPLING_RATE_SENSORS</c> to the generated Android manifest.
    ///
    /// <para>Apps targeting API 31+ are capped at 200 Hz of sensor sampling without this
    /// permission, and ARCore's motion tracking registers the IMU above that. When the
    /// registration is refused, ARCore logs "Failed to register sensor to queue 0", moves its
    /// session to an error state and never resumes it - so the camera is never opened and every
    /// subsequent frame reports "camera was passed NULL". On screen that reads as a black
    /// passthrough with zero planes detected, which looks like a rendering or plane-detection
    /// bug rather than a manifest one.</para>
    ///
    /// <para>Injected here rather than committed as a custom main manifest so Unity and the AR
    /// Foundation packages keep full ownership of the generated file - this is the same approach
    /// the ARCore XR plugin itself uses to add CAMERA and INTERNET. The permission is normal
    /// (install-time, auto-granted), not dangerous, and needs no runtime request.</para>
    /// </summary>
    public class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string PermissionName = "android.permission.HIGH_SAMPLING_RATE_SENSORS";
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[AndroidManifestPostProcessor] No manifest at {manifestPath} - " +
                               $"{PermissionName} was NOT added and ARCore will fail to start.");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var manifest = doc.DocumentElement;
            if (manifest == null)
            {
                Debug.LogError("[AndroidManifestPostProcessor] Manifest has no root element.");
                return;
            }

            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
            {
                if (node.Attributes?["name", AndroidNamespace]?.Value == PermissionName)
                    return;
            }

            var permission = doc.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNamespace, PermissionName);
            manifest.AppendChild(permission);
            doc.Save(manifestPath);

            Debug.Log($"[AndroidManifestPostProcessor] Added {PermissionName} to the Android manifest.");
        }
    }
}
