using UnityEngine;

// Data for a single themed world. Transition logic reads these fields only —
// add worlds here (via WorldCatalog), not inside WorldTransitionManager.
[System.Serializable]
public sealed class WorldDefinition
{
    [SerializeField] string worldName;
    [SerializeField] string iconEmoji;
    [SerializeField] string subtitle;
    [SerializeField] string backgroundTheme;
    [SerializeField] int planetRangeStart;
    [SerializeField] int planetRangeEnd;

    public string WorldName => worldName;
    public string IconEmoji => iconEmoji;
    public string Subtitle => subtitle;
    public string BackgroundTheme => backgroundTheme;
    public int PlanetRangeStart => planetRangeStart;
    public int PlanetRangeEnd => planetRangeEnd;

    public WorldDefinition(
        string worldName,
        string iconEmoji,
        string subtitle,
        string backgroundTheme,
        int planetRangeStart,
        int planetRangeEnd)
    {
        this.worldName = worldName;
        this.iconEmoji = iconEmoji;
        this.subtitle = subtitle;
        this.backgroundTheme = backgroundTheme;
        this.planetRangeStart = planetRangeStart;
        this.planetRangeEnd = planetRangeEnd;
    }

    public string FormattedTitle => $"{worldName.ToUpperInvariant()} WORLD";

    public string FormattedPlanetCounter(int planetInWorld) =>
        $"Planet {planetInWorld} / {PlanetSpawner.PlanetsPerLevel}";
}
