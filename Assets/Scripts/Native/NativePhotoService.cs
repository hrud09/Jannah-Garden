using System;
using System.IO;
using UnityEngine;

namespace NativeIntegration
{
    /// <summary>Which of the two photo buttons a result belongs to.</summary>
    public enum NativePhotoAction
    {
        Share,
        Save
    }

    /// <summary>
    /// Shares a photo through the device's own share sheet and writes it into the device gallery,
    /// without a host app in the middle.
    ///
    /// The game used to hand both jobs to Flutter (see <c>FlutterBridge.RequestSharePhoto</c>), which
    /// meant the Share and Save buttons did nothing at all unless the surrounding app had implemented
    /// the two commands — and nothing in a plain Unity build. Everything here talks to Android and iOS
    /// directly, so the buttons work on any build of the game; the Flutter route stays as a fallback for
    /// anything this class cannot do.
    ///
    /// Android goes through JNI from C# (<see cref="AndroidNativePhoto"/>): a FileProvider URI into
    /// ACTION_SEND for sharing, MediaStore for saving. iOS goes through a small Objective-C plugin
    /// (Assets/Plugins/iOS/JannahNativePhoto.mm): UIActivityViewController and PHPhotoLibrary. The bits
    /// that live outside the C# — the FileProvider declaration, the storage permission, the iOS usage
    /// description and Photos.framework — are all injected at build time by
    /// <c>NativePhotoBuildPostProcessor</c>, so there is nothing to wire up by hand.
    ///
    /// iOS answers asynchronously, from native code, by name: the receiving GameObject must be called
    /// <see cref="GameObjectName"/> and the method <see cref="OnNativePhotoResult"/>. Both are enforced
    /// below — renaming either one silently breaks the iOS callbacks.
    /// </summary>
    public class NativePhotoService : MonoBehaviour
    {
        /// <summary>The name iOS targets with UnitySendMessage(). Must not change.</summary>
        public const string GameObjectName = "NativePhotoService";

        /// <summary>Album the photo is filed under on Android. iOS always saves to the camera roll.</summary>
        public const string DefaultAlbumName = "Jannah Garden";

        /// <summary>
        /// Reported instead of a real failure when the player backs out of the share sheet. Callers
        /// should stay quiet about it — dismissing a share sheet is not an error.
        /// </summary>
        public const string CancelledMessage = "cancelled";

        public static NativePhotoService Instance { get; private set; }

        /// <summary>True on a real phone, where there is a share sheet and a gallery to talk to.</summary>
        public static bool IsSupported =>
            Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer;

        // iOS calls back once per request, so a single slot per action is enough. Cleared as it fires,
        // so a late duplicate from native code cannot invoke a caller twice.
        private static Action<bool, string> _pendingShare;
        private static Action<bool, string> _pendingSave;

        // ─── Bootstrap ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the receiver before the first scene loads. iOS addresses it by name, so it has to
        /// exist before any photo is taken, in whichever scene the game boots into.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            GameObject host = new GameObject(GameObjectName);
            host.AddComponent<NativePhotoService>(); // Awake wires up Instance + DontDestroyOnLoad
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Native code finds this object by name, so a renamed/nested host must be corrected.
            if (gameObject.name != GameObjectName) gameObject.name = GameObjectName;
            if (transform.parent != null) transform.SetParent(null);

            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the device share sheet for a photo already written to disk.
        /// </summary>
        /// <param name="filePath">Absolute path to the image, normally inside persistentDataPath.</param>
        /// <param name="caption">Text offered alongside the image. May be empty.</param>
        /// <param name="onResult">
        /// Called with (success, message) once the platform has answered. A cancelled share sheet comes
        /// back as (false, <see cref="CancelledMessage"/>).
        /// </param>
        /// <returns>False when the request could not even be started, in which case onResult has already fired.</returns>
        public static bool Share(string filePath, string caption, Action<bool, string> onResult)
        {
            if (!FileIsReadable(filePath, onResult)) return false;

            EnsureHost();

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidNativePhoto.Share(filePath, caption, "Share your garden", (ok, message) => Deliver(onResult, ok, message));
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            _pendingShare = onResult;
            IOSNativePhoto.Share(filePath, caption);
            return true;
#else
            Deliver(onResult, false, "Sharing only works on a phone");
            return false;
#endif
        }

        /// <summary>
        /// Writes a photo into the device gallery, asking for whatever permission that needs.
        /// </summary>
        /// <param name="filePath">Absolute path to the image, normally inside persistentDataPath.</param>
        /// <param name="albumName">Album to file it under on Android. Ignored on iOS.</param>
        /// <param name="onResult">Called with (success, message) once the platform has answered.</param>
        /// <returns>False when the request could not even be started, in which case onResult has already fired.</returns>
        public static bool SaveToGallery(string filePath, string albumName, Action<bool, string> onResult)
        {
            if (!FileIsReadable(filePath, onResult)) return false;

            EnsureHost();

            if (string.IsNullOrEmpty(albumName)) albumName = DefaultAlbumName;

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidNativePhoto.SaveToGallery(filePath, albumName, (ok, message) => Deliver(onResult, ok, message));
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            _pendingSave = onResult;
            IOSNativePhoto.SaveToGallery(filePath, albumName);
            return true;
#else
            // No gallery on a desktop, but the Save button should still do something recognisable while
            // the game is being worked on, rather than looking broken in the Editor.
            SaveToPicturesFolder(filePath, albumName, onResult);
            return true;
#endif
        }

        // ─── Native callbacks ─────────────────────────────────────────────────────

        /// <summary>
        /// Called from Objective-C via UnitySendMessage. The name of this method is part of the
        /// contract with JannahNativePhoto.mm — do not rename it.
        /// </summary>
        /// <param name="json">A JSON <see cref="NativePhotoResultMessage"/>.</param>
        public void OnNativePhotoResult(string json)
        {
            NativePhotoResultMessage result;

            try
            {
                result = JsonUtility.FromJson<NativePhotoResultMessage>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativePhoto] Could not read the native result '{json}': {e.Message}");
                return;
            }

            if (result == null)
            {
                Debug.LogWarning($"[NativePhoto] Native result was empty: '{json}'");
                return;
            }

            Debug.Log($"[NativePhoto] {result.action} -> success: {result.success}, message: '{result.message}'");

            bool isSave = string.Equals(result.action, "save", StringComparison.OrdinalIgnoreCase);

            Action<bool, string> callback = isSave ? _pendingSave : _pendingShare;
            if (isSave) _pendingSave = null; else _pendingShare = null;

            Deliver(callback, result.success, result.message);
        }

        // ─── Internals ────────────────────────────────────────────────────────────

        /// <summary>
        /// Makes sure the receiver exists. <see cref="Bootstrap"/> normally covers this, but a photo
        /// taken after a domain reload in the Editor — or after something destroyed the host — would
        /// otherwise leave iOS with nothing to call back into.
        /// </summary>
        private static void EnsureHost()
        {
            if (Instance == null) Bootstrap();
        }

        private static bool FileIsReadable(string filePath, Action<bool, string> onResult)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) return true;

            Debug.LogWarning($"[NativePhoto] No photo at '{filePath}'.");
            Deliver(onResult, false, "The photo is no longer on the device");
            return false;
        }

        private static void Deliver(Action<bool, string> onResult, bool success, string message)
        {
            if (onResult == null) return;

            try
            {
                onResult(success, message);
            }
            catch (Exception e)
            {
                // A throwing callback must not take the rest of the photo flow down with it.
                Debug.LogError($"[NativePhoto] A photo result handler threw: {e}");
            }
        }

        /// <summary>
        /// Editor and desktop stand-in for the gallery: drops the photo in the user's Pictures folder so
        /// the button can be tested without a device.
        /// </summary>
        private static void SaveToPicturesFolder(string filePath, string albumName, Action<bool, string> onResult)
        {
            try
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(pictures)) pictures = Application.persistentDataPath;

                string folder = Path.Combine(pictures, albumName);
                Directory.CreateDirectory(folder);

                string destination = Path.Combine(folder, Path.GetFileName(filePath));
                File.Copy(filePath, destination, true);

                Debug.Log($"[NativePhoto] Photo copied to {destination}");
                Deliver(onResult, true, destination);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativePhoto] Could not copy the photo: {e.Message}");
                Deliver(onResult, false, "Could not save the photo");
            }
        }
    }

    /// <summary>The shape Objective-C sends back through UnitySendMessage.</summary>
    [Serializable]
    public class NativePhotoResultMessage
    {
        /// <summary>"share" or "save".</summary>
        public string action;

        public bool success;

        /// <summary>Empty on success; a reason, or <see cref="NativePhotoService.CancelledMessage"/>, otherwise.</summary>
        public string message;
    }
}
