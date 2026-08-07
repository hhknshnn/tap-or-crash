#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot utility: crops Shop category-tab PNGs to visible alpha bounds under Tabs/Cropped/.
/// Original source PNGs are never modified.
/// </summary>
public static class CropShopTabSprites
{
    const string TabRoot = "Assets/Resources/Shop/UI/Tabs";
    const string CroppedRoot = TabRoot + "/Cropped";

    [MenuItem("Tools/Shop/Crop Tab Sprites To Alpha Bounds")]
    public static void CropAll()
    {
        Directory.CreateDirectory(CroppedRoot);
        string[] files =
        {
            "RocketsTab_Active.png",
            "RocketsTab_Inactive.png",
            "PremiumRocketsTab_Active.png",
            "PremiumRocketsTab_Inactive.png"
        };

        for (int i = 0; i < files.Length; i++)
        {
            string sourcePath = Path.Combine(TabRoot, files[i]);
            if (!File.Exists(sourcePath))
            {
                Debug.LogError("Missing tab sprite: " + sourcePath);
                continue;
            }

            byte[] bytes = File.ReadAllBytes(sourcePath);
            Texture2D readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!readable.LoadImage(bytes))
            {
                Debug.LogError("Failed to load: " + sourcePath);
                Object.DestroyImmediate(readable);
                continue;
            }

            if (!TryGetAlphaBounds(readable, out RectInt bounds))
            {
                Debug.LogError("No visible pixels: " + sourcePath);
                Object.DestroyImmediate(readable);
                continue;
            }

            Texture2D cropped = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false);
            Color[] pixels = readable.GetPixels(bounds.x, bounds.y, bounds.width, bounds.height);
            cropped.SetPixels(pixels);
            cropped.Apply();

            string outName = Path.GetFileNameWithoutExtension(files[i]) + "_Cropped.png";
            string outPath = Path.Combine(CroppedRoot, outName);
            File.WriteAllBytes(outPath, cropped.EncodeToPNG());

            Debug.Log(sourcePath + " canvas=" + readable.width + "x" + readable.height
                + " visible=" + bounds.width + "x" + bounds.height);

            Object.DestroyImmediate(readable);
            Object.DestroyImmediate(cropped);
        }

        AssetDatabase.Refresh();
        ConfigureCroppedImportSettings();
        Debug.Log("Shop tab cropped sprites written to " + CroppedRoot);
    }

    static void ConfigureCroppedImportSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CroppedRoot });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsToUnits = 100f;
            importer.SaveAndReimport();
        }
    }

    static bool TryGetAlphaBounds(Texture2D texture, out RectInt bounds)
    {
        bounds = default;
        int w = texture.width;
        int h = texture.height;
        Color[] pixels = texture.GetPixels();
        int x0 = w, y0 = h, x1 = -1, y1 = -1;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (pixels[y * w + x].a <= 0.05f) continue;
                if (x < x0) x0 = x;
                if (y < y0) y0 = y;
                if (x > x1) x1 = x;
                if (y > y1) y1 = y;
            }
        }

        if (x1 < x0 || y1 < y0) return false;
        bounds = new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        return true;
    }
}
#endif
