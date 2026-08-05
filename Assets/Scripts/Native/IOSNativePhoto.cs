#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace NativeIntegration
{
    /// <summary>
    /// The iOS half of <see cref="NativePhotoService"/> — a thin wrapper over
    /// Assets/Plugins/iOS/JannahNativePhoto.mm.
    ///
    /// Both calls return immediately: UIActivityViewController and PHPhotoLibrary are asynchronous and
    /// answer later through UnitySendMessage, which lands on
    /// <see cref="NativePhotoService.OnNativePhotoResult"/>. The entry point names below are half of that
    /// contract — the other half is in the .mm file, and they have to be changed together.
    /// </summary>
    internal static class IOSNativePhoto
    {
        [DllImport("__Internal")]
        private static extern void _jannahPhotoShare(string filePath, string caption);

        [DllImport("__Internal")]
        private static extern void _jannahPhotoSaveToGallery(string filePath, string albumName);

        internal static void Share(string filePath, string caption)
        {
            _jannahPhotoShare(filePath, caption ?? string.Empty);
        }

        internal static void SaveToGallery(string filePath, string albumName)
        {
            _jannahPhotoSaveToGallery(filePath, albumName ?? string.Empty);
        }
    }
}
#endif
