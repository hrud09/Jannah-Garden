#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Android;

namespace NativeIntegration
{
    /// <summary>
    /// The Android half of <see cref="NativePhotoService"/>, driven straight from C# over JNI so the
    /// game does not have to carry a Java library or an .aar.
    ///
    /// Two Android facts shape everything below:
    ///
    /// 1. A <c>file://</c> URI handed to another app throws FileUriExposedException from API 24 on, so a
    ///    photo can only be shared as a <c>content://</c> URI. That comes from the FileProvider declared
    ///    by NativePhotoBuildPostProcessor; if it is somehow missing, sharing falls back to publishing
    ///    the photo to the gallery and sharing that entry instead of failing outright.
    ///
    /// 2. Scoped storage (API 29+) forbids writing into the public Pictures folder, and MediaStore is the
    ///    only way in — but it needs no permission. Below that, the folder is writable but only with
    ///    WRITE_EXTERNAL_STORAGE. Both paths are implemented; the SDK level picks.
    /// </summary>
    internal static class AndroidNativePhoto
    {
        /// <summary>Must match the authority written into the manifest by NativePhotoBuildPostProcessor.</summary>
        private const string ProviderAuthoritySuffix = ".jannahphotoprovider";

        private const string MimeType = "image/png";

        // android.content.Intent
        private const string ActionSend = "android.intent.action.SEND";
        private const string ExtraStream = "android.intent.extra.STREAM";
        private const string ExtraText = "android.intent.extra.TEXT";
        private const int FlagGrantReadUriPermission = 0x00000001;

        /// <summary>Android 10. The line between MediaStore-only and the old public-folder write.</summary>
        private const int ScopedStorageSdk = 29;

        private static int _sdkInt;

        private static int SdkInt
        {
            get
            {
                if (_sdkInt == 0)
                {
                    using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
                        _sdkInt = version.GetStatic<int>("SDK_INT");
                }

                return _sdkInt;
            }
        }

        // ─── Share ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the system share sheet. Android gives no signal about what the player then did with the
        /// photo, so the result reports only whether the sheet opened.
        /// </summary>
        internal static void Share(string filePath, string caption, string chooserTitle, Action<bool, string> onResult)
        {
            AndroidJavaObject activity = null;
            AndroidJavaObject uri = null;

            try
            {
                activity = GetActivity();
                if (activity == null)
                {
                    onResult(false, "Sharing is unavailable right now");
                    return;
                }

                uri = ResolveShareUri(activity, filePath);
                if (uri == null)
                {
                    onResult(false, "The photo could not be prepared for sharing");
                    return;
                }

                using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
                {
                    Chain(intent, "setAction", ActionSend);
                    Chain(intent, "setType", MimeType);
                    Chain(intent, "putExtra", ExtraStream, uri);

                    if (!string.IsNullOrEmpty(caption)) Chain(intent, "putExtra", ExtraText, caption);

                    // Without this the receiving app is handed a URI it is not allowed to open.
                    Chain(intent, "addFlags", FlagGrantReadUriPermission);

                    using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
                    using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, chooserTitle))
                    {
                        // The chooser is what actually starts, so the grant has to be on it too.
                        Chain(chooser, "addFlags", FlagGrantReadUriPermission);
                        activity.Call("startActivity", chooser);
                    }
                }

                onResult(true, string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativePhoto] Android share failed: {e}");
                onResult(false, "Could not open the share sheet");
            }
            finally
            {
                uri?.Dispose();
                activity?.Dispose();
            }
        }

        /// <summary>
        /// A content:// URI the share sheet can read. The FileProvider is the right answer; the gallery
        /// is the safety net for a build whose manifest never picked the provider up.
        /// </summary>
        private static AndroidJavaObject ResolveShareUri(AndroidJavaObject activity, string filePath)
        {
            try
            {
                using (AndroidJavaObject file = new AndroidJavaObject("java.io.File", filePath))
                using (AndroidJavaClass provider = new AndroidJavaClass("androidx.core.content.FileProvider"))
                {
                    // The manifest builds this authority out of ${applicationId}, which is whatever
                    // package the game actually ends up inside — not necessarily Unity's own bundle id,
                    // as Application.identifier would report when the game is embedded in a host app.
                    // Asking the context keeps the two halves in step.
                    string authority = activity.Call<string>("getPackageName") + ProviderAuthoritySuffix;
                    return provider.CallStatic<AndroidJavaObject>("getUriForFile", activity, authority, file);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NativePhoto] FileProvider unavailable ({e.Message}) — sharing via the gallery instead.");
            }

            return InsertIntoMediaStore(activity, filePath, NativePhotoService.DefaultAlbumName, out _);
        }

        // ─── Save ─────────────────────────────────────────────────────────────────

        /// <summary>Writes the photo into the device gallery, asking for storage permission if the OS is old enough to need it.</summary>
        internal static void SaveToGallery(string filePath, string albumName, Action<bool, string> onResult)
        {
            if (SdkInt >= ScopedStorageSdk)
            {
                SaveNow(filePath, albumName, onResult);
                return;
            }

            if (Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                SaveNow(filePath, albumName, onResult);
                return;
            }

            RequestStoragePermission(
                granted =>
                {
                    if (granted) SaveNow(filePath, albumName, onResult);
                    else onResult(false, "Storage permission is needed to save the photo");
                });
        }

        private static void RequestStoragePermission(Action<bool> onDecided)
        {
            bool answered = false;

            void Answer(bool granted)
            {
                // Android can fire more than one of these; the first decision is the one that counts.
                if (answered) return;
                answered = true;
                onDecided(granted);
            }

            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => Answer(true);
            callbacks.PermissionDenied += _ => Answer(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => Answer(false);

            Permission.RequestUserPermission(Permission.ExternalStorageWrite, callbacks);
        }

        private static void SaveNow(string filePath, string albumName, Action<bool, string> onResult)
        {
            AndroidJavaObject activity = null;

            try
            {
                activity = GetActivity();
                if (activity == null)
                {
                    onResult(false, "Saving is unavailable right now");
                    return;
                }

                string error;

                if (SdkInt >= ScopedStorageSdk)
                {
                    using (AndroidJavaObject item = InsertIntoMediaStore(activity, filePath, albumName, out error))
                    {
                        if (item == null)
                        {
                            onResult(false, string.IsNullOrEmpty(error) ? "Could not save the photo" : error);
                            return;
                        }
                    }
                }
                else if (!SaveToPublicPictures(activity, filePath, albumName, out error))
                {
                    onResult(false, string.IsNullOrEmpty(error) ? "Could not save the photo" : error);
                    return;
                }

                onResult(true, string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativePhoto] Android save failed: {e}");
                onResult(false, "Could not save the photo");
            }
            finally
            {
                activity?.Dispose();
            }
        }

        /// <summary>
        /// Adds the photo to the gallery through MediaStore and returns the new entry's URI. This is the
        /// only route on API 29+, and it needs no permission because the app only ever writes into the
        /// row the media provider just handed it.
        /// </summary>
        private static AndroidJavaObject InsertIntoMediaStore(AndroidJavaObject activity, string filePath, string albumName, out string error)
        {
            error = null;
            AndroidJavaObject item = null;

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);

                using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (AndroidJavaObject values = new AndroidJavaObject("android.content.ContentValues"))
                using (AndroidJavaClass media = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
                using (AndroidJavaObject collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                {
                    values.Call("put", "_display_name", Path.GetFileName(filePath));
                    values.Call("put", "mime_type", MimeType);

                    // RELATIVE_PATH only exists from API 29; below that the folder comes from where the
                    // file is actually written, which SaveToPublicPictures handles instead.
                    if (SdkInt >= ScopedStorageSdk && !string.IsNullOrEmpty(albumName))
                        values.Call("put", "relative_path", "Pictures/" + albumName);

                    item = resolver.Call<AndroidJavaObject>("insert", collection, values);
                    if (item == null)
                    {
                        error = "The gallery would not accept the photo";
                        return null;
                    }

                    using (AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", item))
                    {
                        if (stream == null)
                        {
                            error = "The gallery would not accept the photo";
                            item.Dispose();
                            return null;
                        }

                        stream.Call("write", bytes);
                        stream.Call("flush");
                        stream.Call("close");
                    }
                }

                Debug.Log($"[NativePhoto] Photo added to the gallery from {filePath}");
                return item;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativePhoto] MediaStore write failed: {e}");
                error = "Could not save the photo";
                item?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// The pre-Android-10 route: copy the file into the public Pictures folder and tell the media
        /// scanner, so the gallery notices it. Requires WRITE_EXTERNAL_STORAGE, which the caller has
        /// already obtained.
        /// </summary>
        private static bool SaveToPublicPictures(AndroidJavaObject activity, string filePath, string albumName, out string error)
        {
            error = null;

            try
            {
                string destination;

                using (AndroidJavaClass environment = new AndroidJavaClass("android.os.Environment"))
                using (AndroidJavaObject pictures = environment.CallStatic<AndroidJavaObject>(
                           "getExternalStoragePublicDirectory", environment.GetStatic<string>("DIRECTORY_PICTURES")))
                {
                    string root = pictures.Call<string>("getAbsolutePath");
                    string folder = string.IsNullOrEmpty(albumName) ? root : Path.Combine(root, albumName);

                    Directory.CreateDirectory(folder);

                    destination = Path.Combine(folder, Path.GetFileName(filePath));
                    File.Copy(filePath, destination, true);
                }

                try
                {
                    using (AndroidJavaClass scanner = new AndroidJavaClass("android.media.MediaScannerConnection"))
                        scanner.CallStatic("scanFile", activity, new[] { destination }, new[] { MimeType }, null);
                }
                catch (Exception e)
                {
                    // The photo is on the device either way; it may just take a gallery refresh to show up.
                    Debug.LogWarning($"[NativePhoto] Media scan failed: {e.Message}");
                }

                Debug.Log($"[NativePhoto] Photo copied to {destination}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NativePhoto] Public folder write failed: {e}");
                error = "Could not save the photo";
                return false;
            }
        }

        // ─── JNI helpers ──────────────────────────────────────────────────────────

        private static AndroidJavaObject GetActivity()
        {
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        /// <summary>
        /// Calls a builder-style Java method and throws the returned reference away. Intent's setters all
        /// return the same Intent back; without this every call would leak a JNI reference.
        /// </summary>
        private static void Chain(AndroidJavaObject target, string method, params object[] args)
        {
            using (AndroidJavaObject ignored = target.Call<AndroidJavaObject>(method, args)) { }
        }
    }
}
#endif
