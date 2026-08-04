using UnityEngine;

// Where the Main Menu's art comes from once it has been baked.
//
// UIGlass, UIStyleKit, MenuShowcaseAssets and VfxSpriteFactory can all build their sprites
// in memory. That is fine for something drawn only in Play Mode, but an in-memory Sprite
// has no asset path, so a scene or prefab can only store null for it — which is why the
// approved menu could not be serialized at all.
//
// MenuArtBaker writes those sprites out as real assets under Resources/MenuBaked, and the
// factories look here first. The pixels are identical either way; the difference is that a
// baked sprite has an asset path and can therefore be referenced by the serialized menu.
//
// The name mapping lives here so the baker and the loaders can never disagree about what a
// sprite is called on disk.
public static class MenuBakedArt
{
    public const string Folder = "MenuBaked/";
    public const string MaterialFolder = Folder + "Materials/";

    // File names have to survive being a path, and several generated sprites carry spaces
    // in their name ("Menu Showcase Star Flare"). Everything that is not alphanumeric
    // becomes an underscore, on both the writing and the reading side.
    public static string AssetName(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return "Unnamed";

        var builder = new System.Text.StringBuilder(spriteName.Length);
        foreach (char c in spriteName) builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        return builder.ToString();
    }

    public static Sprite Load(string spriteName)
        => Resources.Load<Sprite>(Folder + AssetName(spriteName));

    public static Material LoadMaterial(string materialName)
        => Resources.Load<Material>(MaterialFolder + AssetName(materialName));
}
