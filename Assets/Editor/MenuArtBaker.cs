using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Turns the Main Menu's runtime-generated art into real project assets.
//
// The menu's glass surfaces, star flares, vignette, soft glows and particle materials are
// all built in memory at Play time by UIGlass, UIStyleKit, MenuShowcaseAssets and
// VfxSpriteFactory. An in-memory Sprite has no asset path, so a prefab or scene can only
// store null for it — which is why the approved menu could never be serialized.
//
// This baker writes those objects out once, pixel for pixel, so the serialized menu can
// reference them. It discovers what to bake by scanning the live menu rather than from a
// hardcoded list: whatever the approved menu actually renders is exactly what gets baked,
// and a layer added later is picked up without touching this file.
//
// Run it from Tools ▸ Tap or Crash ▸ Bake Menu Art while the menu is on screen in Play
// Mode. It is an editor tool: nothing here ships in a build.
public static class MenuArtBaker
{
    public const string SpriteFolder = "Assets/Resources/MenuBaked";
    public const string MaterialFolder = "Assets/Resources/MenuBaked/Materials";

    // The roots that make up the approved menu. Scanned in this order.
    static readonly string[] StageRoots = { "MainMenu" };
    const string CanvasName = "Canvas";
    const string PanelName = "StartPanel";

    [MenuItem("Tools/Tap or Crash/Bake Menu Art")]
    public static void Bake()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Bake Menu Art",
                "Enter Play Mode with the Main Menu on screen first.\n\n" +
                "The art this bakes only exists once the menu has built itself.", "OK");
            return;
        }

        Directory.CreateDirectory(SpriteFolder);
        Directory.CreateDirectory(MaterialFolder);

        var sprites = new Dictionary<Sprite, string>();
        var materials = new Dictionary<Material, string>();

        foreach (string rootName in StageRoots)
        {
            GameObject root = GameObject.Find(rootName);
            if (root != null) Collect(root.transform, sprites, materials);
        }

        GameObject canvas = GameObject.Find(CanvasName);
        Transform panel = canvas != null ? canvas.transform.Find(PanelName) : null;
        if (panel != null) Collect(panel, sprites, materials);

        int written = 0;
        var imported = new List<KeyValuePair<string, Sprite>>();

        // Two passes on purpose. Writing the files is batched, but the import settings are
        // not: SaveAndReimport is swallowed inside a StartAssetEditing block, which left
        // every sprite on the importer's default 100 pixels per unit and no 9-slice border.
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var pair in sprites)
            {
                string path = WriteSprite(pair.Key, pair.Value);
                if (path == null) continue;
                imported.Add(new KeyValuePair<string, Sprite>(path, pair.Key));
                written++;
            }
            foreach (var pair in materials) if (WriteMaterial(pair.Key, pair.Value)) written++;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        foreach (var pair in imported) ApplyImportSettings(pair.Key, pair.Value);
        AssetDatabase.Refresh();

        Debug.Log($"MenuArtBaker: baked {written} asset(s) into {SpriteFolder}. " +
                  "Exit Play Mode, then re-enter so the menu picks the baked art up.");
    }

    // ── discovery ────────────────────────────────────────────────────────────

    static void Collect(Transform root, Dictionary<Sprite, string> sprites,
        Dictionary<Material, string> materials)
    {
        foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            Add(sprites, renderer.sprite);
            Add(materials, renderer.sharedMaterial);
        }

        foreach (UnityEngine.UI.Image image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            Add(sprites, image.sprite);

        foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            Add(materials, renderer.sharedMaterial);
    }

    // Only objects with no asset path are baked. Anything already imported — Lava_06,
    // the Space PNGs, the icon family — is left exactly as it is.
    static void Add<T>(Dictionary<T, string> target, T asset) where T : Object
    {
        if (asset == null || target.ContainsKey(asset)) return;
        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset))) return;

        // MenuBakedArt owns the mapping, so what is written here is exactly what the
        // factories look for at load time.
        string name = MenuBakedArt.AssetName(asset.name);
        if (target.ContainsValue(name))
        {
            Debug.LogError($"MenuArtBaker: two different runtime assets both bake to '{name}'. " +
                           "Give them distinct names at their source before baking.", asset);
            return;
        }

        target.Add(asset, name);
    }

    // ── writing ──────────────────────────────────────────────────────────────

    // Returns the asset path so the caller can configure the importer once the batch is
    // closed, or null when the texture could not be read back.
    static string WriteSprite(Sprite sprite, string name)
    {
        Texture2D readable = ReadBack(sprite.texture);
        if (readable == null) return null;

        string path = Path.Combine(SpriteFolder, name + ".png").Replace('\\', '/');
        File.WriteAllBytes(path, readable.EncodeToPNG());
        Object.DestroyImmediate(readable);
        return path;
    }

    // The generators call Apply(false, true) on some textures, which drops the CPU copy
    // and makes GetPixels throw. Going through a RenderTexture reads them back off the
    // GPU instead, so every texture bakes the same way whether it is readable or not.
    static Texture2D ReadBack(Texture source)
    {
        if (source == null) return null;

        RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;

        Graphics.Blit(source, target);
        RenderTexture.active = target;

        Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
        readable.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        return readable;
    }

    // The baked asset has to import back as the exact sprite the generator produced:
    // same pivot, same pixels-per-unit and — for the 9-sliced glass panels — the same
    // border, or the stretched middle band smears.
    static void ApplyImportSettings(string path, Sprite source)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // Everything sprite-shaped goes through TextureImporterSettings in one write.
        // Setting importer.spritePixelsPerUnit / spriteBorder directly and *then* calling
        // SetTextureSettings silently puts both back to the importer defaults — which
        // imported every baked sprite at 100 pixels per unit and resized the whole stage.
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureType = TextureImporterType.Sprite;
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteExtrude = 0;
        // The two that decide how big the sprite is in the world and how a 9-slice
        // stretches. Both come from the sprite the generator produced.
        settings.spritePixelsPerUnit = source.pixelsPerUnit;
        settings.spriteBorder = source.border;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    static bool WriteMaterial(Material material, string name)
    {
        string path = Path.Combine(MaterialFolder, name + ".mat").Replace('\\', '/');
        var copy = new Material(material);
        AssetDatabase.CreateAsset(copy, path);
        return true;
    }
}
