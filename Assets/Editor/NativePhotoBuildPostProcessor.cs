using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

#if UNITY_ANDROID
using System.Xml;
using UnityEditor.Android;
#endif

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace NativeIntegration.EditorTools
{
    /// <summary>
    /// Shared settings between the two build steps that make the Share and Save buttons work.
    /// </summary>
    internal static class NativePhotoBuildSettings
    {
        /// <summary>Must match AndroidNativePhoto.ProviderAuthoritySuffix.</summary>
        internal const string ProviderAuthoritySuffix = ".jannahphotoprovider";

        /// <summary>Own resource name, so this cannot collide with a host app's own file_paths.xml.</summary>
        internal const string FilePathsResource = "jannah_photo_paths";

        /// <summary>Shown by iOS above the "add to your photos" prompt.</summary>
        internal const string PhotoLibraryAddUsageDescription =
            "Jannah Garden saves the photos you take of your garden to your gallery.";
    }

#if UNITY_ANDROID

    /// <summary>
    /// Adds the two things Android needs before the game can share or save a photo, straight into the
    /// generated Gradle project.
    ///
    /// Android will not let one app hand a <c>file://</c> path to another, so a photo can only be shared
    /// through a FileProvider declared in the manifest — and a FileProvider needs an XML resource saying
    /// which folders it may serve. Neither can be expressed from C#.
    ///
    /// This runs against Unity's own generated manifest and only ever adds to it, which is why there is
    /// no AndroidManifest.xml checked into Assets/Plugins/Android: a custom manifest would replace
    /// Unity's and have to be kept in step with it by hand for the life of the project.
    ///
    /// Failures are warnings, not errors. A broken share button is not worth failing a release build
    /// over — but watch for these in the build log if sharing stops working after a Unity upgrade.
    /// </summary>
    public class NativePhotoAndroidPostProcessor : IPostGenerateGradleAndroidProject
    {
        /// <summary>Late, so anything else that rewrites the manifest has already had its turn.</summary>
        public int callbackOrder => 100;

        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                string moduleRoot = FindModuleRoot(path);

                if (moduleRoot == null)
                {
                    Debug.LogWarning($"[NativePhoto] No AndroidManifest.xml under {path} — sharing will fall back to the gallery.");
                    return;
                }

                WriteFileProviderPaths(moduleRoot);
                PatchManifest(moduleRoot);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NativePhoto] Could not prepare the Android project for photo sharing: {e}");
            }
        }

        /// <summary>
        /// The module holding the manifest. Unity hands over the unityLibrary directory itself, but the
        /// exported-project layouts have moved around between versions, so the parent is checked too.
        /// </summary>
        private static string FindModuleRoot(string path)
        {
            if (File.Exists(Path.Combine(path, "src", "main", "AndroidManifest.xml"))) return path;

            string nested = Path.Combine(path, "unityLibrary");
            if (File.Exists(Path.Combine(nested, "src", "main", "AndroidManifest.xml"))) return nested;

            return null;
        }

        /// <summary>
        /// The folders the FileProvider is allowed to serve from. persistentDataPath is the internal
        /// files directory by default and the external one when the player's Write Permission is set to
        /// External, so both are listed rather than guessing which build this is.
        /// </summary>
        private static void WriteFileProviderPaths(string moduleRoot)
        {
            string folder = Path.Combine(moduleRoot, "src", "main", "res", "xml");
            Directory.CreateDirectory(folder);

            string file = Path.Combine(folder, NativePhotoBuildSettings.FilePathsResource + ".xml");

            File.WriteAllText(file,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<paths>\n" +
                "    <files-path name=\"jannah_internal_files\" path=\".\" />\n" +
                "    <cache-path name=\"jannah_internal_cache\" path=\".\" />\n" +
                "    <external-path name=\"jannah_external\" path=\".\" />\n" +
                "    <external-files-path name=\"jannah_external_files\" path=\".\" />\n" +
                "    <external-cache-path name=\"jannah_external_cache\" path=\".\" />\n" +
                "</paths>\n");
        }

        private static void PatchManifest(string moduleRoot)
        {
            string manifestPath = Path.Combine(moduleRoot, "src", "main", "AndroidManifest.xml");

            XmlDocument document = new XmlDocument();
            document.Load(manifestPath);

            XmlElement manifest = document.DocumentElement;
            if (manifest == null) return;

            bool changed = AddStoragePermission(document, manifest);
            changed |= AddFileProvider(document, manifest);

            if (!changed) return;

            document.Save(manifestPath);
            Debug.Log("[NativePhoto] Manifest prepared for photo sharing and gallery saving.");
        }

        /// <summary>
        /// Android 9 and below cannot write into the public Pictures folder without this. Capped at API
        /// 28 because from Android 10 the game saves through MediaStore, which needs no permission — and
        /// asking for storage access the app does not use is a Play Store review problem.
        /// </summary>
        private static bool AddStoragePermission(XmlDocument document, XmlElement manifest)
        {
            const string permission = "android.permission.WRITE_EXTERNAL_STORAGE";

            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
            {
                if (GetAndroidAttribute(node, "name") == permission) return false;
            }

            XmlElement element = document.CreateElement("uses-permission");
            SetAndroidAttribute(document, element, "name", permission);
            SetAndroidAttribute(document, element, "maxSdkVersion", "28");
            manifest.AppendChild(element);

            return true;
        }

        /// <summary>
        /// Declares the provider that turns the photo's path into a content:// URI the share sheet may
        /// read. The authority is built from ${applicationId} so it stays unique — and correct — whether
        /// the game ships on its own or inside a host app with a different package name.
        /// </summary>
        private static bool AddFileProvider(XmlDocument document, XmlElement manifest)
        {
            XmlElement application = manifest.SelectSingleNode("application") as XmlElement;

            if (application == null)
            {
                Debug.LogWarning("[NativePhoto] Manifest has no <application> — the FileProvider was not added.");
                return false;
            }

            string authority = "${applicationId}" + NativePhotoBuildSettings.ProviderAuthoritySuffix;

            foreach (XmlNode node in application.SelectNodes("provider"))
            {
                if (GetAndroidAttribute(node, "authorities") == authority) return false;
            }

            XmlElement provider = document.CreateElement("provider");
            SetAndroidAttribute(document, provider, "name", "androidx.core.content.FileProvider");
            SetAndroidAttribute(document, provider, "authorities", authority);
            SetAndroidAttribute(document, provider, "exported", "false");
            SetAndroidAttribute(document, provider, "grantUriPermissions", "true");

            XmlElement metaData = document.CreateElement("meta-data");
            SetAndroidAttribute(document, metaData, "name", "android.support.FILE_PROVIDER_PATHS");
            SetAndroidAttribute(document, metaData, "resource", "@xml/" + NativePhotoBuildSettings.FilePathsResource);
            provider.AppendChild(metaData);

            application.AppendChild(provider);

            return true;
        }

        private static string GetAndroidAttribute(XmlNode node, string name)
        {
            return (node as XmlElement)?.GetAttribute(name, AndroidNamespace);
        }

        private static void SetAndroidAttribute(XmlDocument document, XmlElement element, string name, string value)
        {
            // Built with the prefix explicitly: SetAttribute(name, namespace, value) would invent a fresh
            // xmlns prefix rather than reusing the manifest's own "android".
            XmlAttribute attribute = document.CreateAttribute("android", name, AndroidNamespace);
            attribute.Value = value;
            element.Attributes.Append(attribute);
        }
    }

#endif // UNITY_ANDROID

    /// <summary>
    /// The iOS counterpart: links Photos.framework, which JannahNativePhoto.mm needs, and writes the
    /// usage description iOS demands before an app may add anything to the photo library. Without that
    /// key the app is terminated at the permission prompt rather than simply refused.
    /// </summary>
    public static class NativePhotoIOSPostProcessor
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string builtProjectPath)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS) return;

            try
            {
                AddPhotoLibraryUsageDescription(builtProjectPath);
                LinkPhotosFramework(builtProjectPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NativePhoto] Could not prepare the Xcode project for photo saving: {e}");
            }
#endif
        }

#if UNITY_IOS

        /// <summary>
        /// Note that when the game is embedded in a host app, the Info.plist that counts at runtime is
        /// the host's — this covers the Unity project, and the same key has to be added on the host side.
        /// </summary>
        private static void AddPhotoLibraryUsageDescription(string builtProjectPath)
        {
            string plistPath = Path.Combine(builtProjectPath, "Info.plist");

            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[NativePhoto] No Info.plist at {plistPath} — add NSPhotoLibraryAddUsageDescription to the host app by hand.");
                return;
            }

            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            const string key = "NSPhotoLibraryAddUsageDescription";

            // Anything already there was a deliberate choice; do not overwrite it.
            if (plist.root.values.ContainsKey(key)) return;

            plist.root.SetString(key, NativePhotoBuildSettings.PhotoLibraryAddUsageDescription);
            plist.WriteToFile(plistPath);

            Debug.Log("[NativePhoto] Added NSPhotoLibraryAddUsageDescription to Info.plist.");
        }

        private static void LinkPhotosFramework(string builtProjectPath)
        {
            string projectPath = PBXProject.GetPBXProjectPath(builtProjectPath);

            if (!File.Exists(projectPath))
            {
                Debug.LogWarning($"[NativePhoto] No Xcode project at {projectPath} — link Photos.framework by hand.");
                return;
            }

            PBXProject project = new PBXProject();
            project.ReadFromFile(projectPath);

            // The plugin compiles into UnityFramework, so that is the target that has to link Photos.
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Photos.framework", false);
            project.WriteToFile(projectPath);

            Debug.Log("[NativePhoto] Linked Photos.framework.");
        }

#endif // UNITY_IOS
    }
}
