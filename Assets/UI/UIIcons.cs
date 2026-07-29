using System.Collections.Generic;
using UnityEngine;

// Access to the baked icon family in Resources/Icons.
//
// The icons are rendered by Tools/bake_ui_icons.py — one Blender stage, one
// camera, one light rig for all of them. Re-bake there, never hand-edit here.
public static class UIIcons
{
    public const string Coin = "coin";
    public const string Moon = "moon";
    public const string Sun = "sun";
    public const string SoundOn = "sound_on";
    public const string SoundOff = "sound_off";
    public const string Help = "help";
    public const string Shop = "shop";
    public const string Settings = "settings";
    public const string Pause = "pause";
    public const string Star = "star";
    public const string Close = "close";

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (cache.TryGetValue(name, out Sprite cached) && cached != null) return cached;

        string path = "Icons/icon_" + name;
        Sprite sprite = Resources.Load<Sprite>(path);

        // A project that imports these as plain textures still gets its icons.
        if (sprite == null)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture != null)
            {
                sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = "Icon_" + name;
            }
        }

        if (sprite == null)
            Debug.LogWarning("UIIcons: missing Resources/" + path + " — re-run Tools/bake_ui_icons.py");

        cache[name] = sprite;
        return sprite;
    }
}
