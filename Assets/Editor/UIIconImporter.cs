using UnityEditor;
using UnityEngine;

// Keeps the baked UI icon family importing as one sprite per file.
//
// Every icon in Resources/Icons had drifted to Sprite Mode: Multiple, sliced on
// alpha islands. Resources.Load<Sprite> returns the *first* sub-sprite of such a
// texture, so the UI was drawing fragments: one ray of the nine-piece sun, the
// handle off the two-piece shop bag, one bar of the two-piece pause glyph. That
// is what made the buttons read as clipart from three different games — the
// renders themselves were always right.
//
// Single mode also restores the family's shared framing: the bake gives every
// icon the same padding inside a 1.70-unit square, which only survives if Unity
// keeps the full 256px frame instead of cropping each one to its own content.
public sealed class UIIconImporter : AssetPostprocessor
{
    const string IconFolder = "Assets/Resources/Icons/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(IconFolder)) return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
    }
}
