#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot utility: crops Shop header PNGs to visible alpha bounds under Header/Cropped/.
/// Original source PNGs are never modified.
/// </summary>
public static class CropShopHeaderSprites
{
    const string HeaderRoot = "Assets/Resources/Shop/UI/Header";
    const string CroppedRoot = HeaderRoot + "/Cropped";

    [MenuItem("Tools/Shop/Prepare BalanceChipBaseFlat Cropped")]
    public static void PrepareBalanceChipBaseFlat()
    {
        Directory.CreateDirectory(CroppedRoot);
        string sourcePath = Path.Combine(HeaderRoot, "BalanceChipBaseFlat.png");
        if (!File.Exists(sourcePath))
        {
            Debug.LogError("Missing header sprite: " + sourcePath);
            return;
        }

        byte[] bytes = File.ReadAllBytes(sourcePath);
        Texture2D readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!readable.LoadImage(bytes))
        {
            Debug.LogError("Failed to load: " + sourcePath);
            Object.DestroyImmediate(readable);
            return;
        }

        if (!TryGetAlphaBounds(readable, out RectInt bounds))
        {
            Debug.LogError("No visible pixels: " + sourcePath);
            Object.DestroyImmediate(readable);
            return;
        }

        Texture2D cropped = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false);
        Color[] pixels = readable.GetPixels(bounds.x, bounds.y, bounds.width, bounds.height);
        cropped.SetPixels(pixels);
        cropped.Apply();

        const string outAssetPath = "Assets/Resources/Shop/UI/Header/Cropped/BalanceChipBaseFlat_Cropped.png";
        string outPath = Path.Combine(CroppedRoot, "BalanceChipBaseFlat_Cropped.png");
        File.WriteAllBytes(outPath, cropped.EncodeToPNG());
        Object.DestroyImmediate(readable);
        Object.DestroyImmediate(cropped);

        AssetDatabase.Refresh();

        var importer = AssetImporter.GetAtPath(outAssetPath) as TextureImporter;
        if (importer != null)
        {
            float endCapX = bounds.height * 0.5f;
            // Thin equal top/bottom rims — only the flat center band stretches vertically.
            float endCapY = Mathf.Max(8f, bounds.height * 0.075f);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsToUnits = 100f;
            importer.spriteBorder = new Vector4(endCapX, endCapY, endCapX, endCapY);
            importer.SaveAndReimport();
            Debug.Log("BalanceChipBaseFlat cropped " + bounds.width + "x" + bounds.height
                + " borders=(" + endCapX + "," + endCapY + "," + endCapX + "," + endCapY + ")");
        }
    }

    [MenuItem("Tools/Shop/Crop Header Sprites To Alpha Bounds")]
    public static void CropAll()
    {
        Directory.CreateDirectory(CroppedRoot);
        string[] files =
        {
            "ShopTitleCluster.png",
            "CloseButtonShell.png",
            "BalanceChipBase.png",
            "DiamondIcon.png",
            "PlusButton.png"
        };

        for (int i = 0; i < files.Length; i++)
        {
            string sourcePath = Path.Combine(HeaderRoot, files[i]);
            if (!File.Exists(sourcePath))
            {
                Debug.LogError("Missing header sprite: " + sourcePath);
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
                + " visible=" + bounds.width + "x" + bounds.height
                + " bounds=(" + bounds.x + "," + bounds.y + ")-(" + (bounds.xMax - 1) + "," + (bounds.yMax - 1) + ")");

            Object.DestroyImmediate(readable);
            Object.DestroyImmediate(cropped);
        }

        AssetDatabase.Refresh();
        ConfigureCroppedImportSettings();
        Debug.Log("Shop header cropped sprites written to " + CroppedRoot);
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
