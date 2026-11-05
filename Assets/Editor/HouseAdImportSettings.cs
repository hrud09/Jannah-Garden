using UnityEditor;

/// <summary>
/// Imports anything in <c>Assets/Resources/HouseAds</c> the way <see cref="HouseAdPresenter"/> needs it.
/// An image left on Unity's default texture type is not a sprite, so the presenter would not find it and
/// would fall through to the no-ad path with no error.
/// </summary>
public class HouseAdImportSettings : AssetPostprocessor
{
    private const string Folder = "Assets/Resources/HouseAds/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
