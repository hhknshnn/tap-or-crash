using UnityEngine;
using UnityEngine.UI;

// The single source of truth for how this game's UI looks.
//
// Nothing else picks a colour, a corner radius or a font size by hand. When the
// coin panel, the sound disc and the launch pill all read their values from one
// place, they stop looking like three projects — that is the whole idea.
//
// World inheritance: only the *hue* travels. The active world pushes the glass
// and the rim toward its own colour, but saturation and value stay pinned to
// the values below, so Lava never turns the HUD muddy and Ice never washes it
// out. The call to action keeps the thruster orange in every world: a premium
// game has exactly one "press this" colour and never moves it.
public static class UIDesign
{
    // The deterministic palette baked into the Edit Mode Main Menu preview.
    // Runtime still resolves the player's persisted/current world in Refresh().
    public const string ApprovedStartupWorld = "Alien";

    public struct Palette
    {
        public Color Accent;
        public Color Glass;
        public Color GlassDeep;
        public Color GlassRim;
        public Color Scrim;
    }

    // ── Geometry ─────────────────────────────────────────────────────────────
    // One distance from every screen edge. The coin chip, the icon discs and the
    // shop pill used to sit at 30, 36 and 40 pixels of inset, which is enough to
    // read as three separate layouts sharing a screen.
    public const float ScreenMargin = 40f;

    // One radius family. Pills and cards differ in size, never in roundness per
    // unit of height, which is what makes a row of mismatched controls settle.
    public const float RadiusCard = 34f;
    public const float RadiusPill = 28f;
    public const float RadiusChip = 22f;

    public const float ChipHeight = 76f;           // coin counter, best score
    public const float ButtonHeightMajor = 104f;   // primary actions
    public const float ButtonHeightPill = 92f;     // secondary pills
    public const float IconButtonSize = 112f;      // the shared disc
    public const float IconGlyphRatio = 0.52f;     // glyph as a share of the disc

    // ── Type scale ───────────────────────────────────────────────────────────
    // A fourth-based scale with tracking that loosens as size drops: small caps
    // need air to stay readable on a phone, display type needs it removed.
    public const float TypeDisplay = 74f;
    public const float TypeTitle = 52f;
    public const float TypeHeading = 34f;
    public const float TypeButton = 30f;
    public const float TypeBody = 25f;
    public const float TypeLabel = 21f;
    public const float TypeCaption = 17f;
    public const float TypeMicro = 13f;

    public const float TrackDisplay = 3f;
    public const float TrackTitle = 2.5f;
    public const float TrackButton = 4f;
    public const float TrackLabel = 6f;
    public const float TrackCaption = 8f;
    public const float TrackMicro = 10f;

    // ── Fixed brand colours ──────────────────────────────────────────────────
    // Deep space ink: every glass surface is a tint of this, never of black.
    static readonly Color Ink = new Color(0.030f, 0.045f, 0.105f);

    public static readonly Color Cta = new Color(1.000f, 0.560f, 0.180f);       // thruster orange
    public static readonly Color CtaText = new Color(1.000f, 0.845f, 0.560f);
    public static readonly Color Gold = new Color(1.000f, 0.760f, 0.240f);      // coin / best
    public static readonly Color Danger = new Color(0.960f, 0.360f, 0.280f);

    public static readonly Color TextMain = new Color(0.960f, 0.972f, 1.000f);
    public static readonly Color TextSub = new Color(0.720f, 0.780f, 0.890f);
    public static readonly Color TextMuted = new Color(0.560f, 0.630f, 0.760f);

    static readonly Color DefaultAccent = new Color(0.340f, 0.860f, 1.000f);

    // ── World-derived colours ────────────────────────────────────────────────

    public static Color Accent { get; private set; } = DefaultAccent;
    public static Color Glass { get; private set; }
    public static Color GlassDeep { get; private set; }
    public static Color GlassRim { get; private set; }
    public static Color Scrim { get; private set; }

    /// Bumped whenever the palette actually moves. Tinted graphics compare an
    /// int rather than re-deriving colours every frame.
    public static int Version { get; private set; }

    public static string ActiveWorld { get; private set; }

    public static Palette CurrentPalette
    {
        get
        {
            EnsureInitialised();
            return new Palette
            {
                Accent = Accent,
                Glass = Glass,
                GlassDeep = GlassDeep,
                GlassRim = GlassRim,
                Scrim = Scrim,
            };
        }
    }

    public static Palette ApprovedStartupPalette => PaletteForWorld(ApprovedStartupWorld);

    static bool initialised;

    public static void EnsureInitialised()
    {
        if (!initialised) Refresh();
    }

    /// Re-resolves the palette from the world the player is currently in.
    /// Cheap enough to poll: it only allocates when the world name changes.
    public static void Refresh()
    {
        string world = ResolveWorldName();
        if (initialised && world == ActiveWorld) return;

        ActiveWorld = world;
        initialised = true;

        Palette palette = PaletteForWorld(world);
        Accent = palette.Accent;
        Glass = palette.Glass;
        GlassDeep = palette.GlassDeep;
        GlassRim = palette.GlassRim;
        Scrim = palette.Scrim;

        Version++;
    }

    // Pure palette resolution shared by runtime and deterministic authoring. It
    // never reads PlayerPrefs and is therefore safe for editor preview baking.
    public static Palette PaletteForWorld(string world)
    {
        Palette palette = new Palette();

        Color raw = PlanetAmbience.AccentColorFor(world, DefaultAccent);
        Color.RGBToHSV(raw, out float hue, out float saturation, out _);

        // A world whose accent is nearly grey has no hue worth inheriting;
        // falling back keeps the UI from drifting to a dead neutral.
        if (saturation < 0.08f) Color.RGBToHSV(DefaultAccent, out hue, out _, out _);

        // Accent: the world's hue at the system's own saturation and value, so
        // every world's UI accent carries identical weight on screen.
        palette.Accent = Color.HSVToRGB(hue, 0.52f, 0.98f);

        // Enough value to read as a lit surface. Any darker and the fill
        // disappears against space and the rim becomes a drawn outline, which
        // is the opposite of glass.
        Color hueTint = Color.HSVToRGB(hue, 0.55f, 0.30f);
        palette.Glass = Tint(Color.Lerp(Ink, hueTint, 0.38f), 0.84f);
        palette.GlassDeep = Tint(Color.Lerp(Ink, hueTint, 0.22f), 0.95f);
        palette.GlassRim = Tint(Color.HSVToRGB(hue, 0.42f, 1.00f), 0.34f);
        palette.Scrim = Tint(Color.Lerp(Ink, hueTint, 0.12f), 0.86f);
        return palette;
    }

    static Color Tint(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    // In a run, the world under the rocket right now. Otherwise the furthest
    // world reached, which is the one the menu is showing off.
    static string ResolveWorldName()
    {
        string[] names = PlanetSpawner.LevelNames;
        if (names == null || names.Length == 0) return null;

        string pinned = PlayerPrefs.GetString(MenuShowcaseTheme.SelectedThemeKey, string.Empty).Trim();
        if (pinned.Length > 0) return pinned;

        // GameManager exists on the menu too, and its live score is zero there —
        // reading it unconditionally pinned the whole interface to world one
        // while the stage behind it showed the player's furthest world. The
        // selection rule below is now literally MenuShowcaseTheme's, so the
        // chrome and the planet it sits on can no longer disagree.
        int score = GameManager.isGameStarted && GameManager.instance != null
            ? GameManager.instance.GetScore()
            : PlayerPrefs.GetInt("HighScore", 0);

        int index = Mathf.Clamp(PlanetSpawner.LevelIndexForScore(score), 0, names.Length - 1);
        return names[index];
    }
}
