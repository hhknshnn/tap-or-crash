using UnityEngine;

// Single registry for world metadata. Future worlds are added here only.
public static class WorldCatalog
{
    static readonly WorldDefinition[] Worlds =
    {
        new WorldDefinition("Natural",    "🌍", "A peaceful beginning.",       "Natural",    0,  9),
        new WorldDefinition("Ice",        "❄️", "Everything freezes.",         "Ice",        10, 19),
        new WorldDefinition("Lava",       "🌋", "The heat is rising.",         "Lava",       20, 29),
        new WorldDefinition("Desert",     "🏜️", "Beyond the endless dunes.",   "Desert",     30, 39),
        new WorldDefinition("Ocean",      "🌊", "Ride the tides.",             "Ocean",      40, 49),
        new WorldDefinition("Crystal",    "💎", "Shining from within.",        "Crystal",    50, 59),
        new WorldDefinition("Sakura",     "🌸", "Petals in the wind.",         "Sakura",     60, 69),
        new WorldDefinition("Mushroom",   "🍄", "Life grows everywhere.",      "Mushroom",   70, 79),
        new WorldDefinition("Cloud",      "☁️", "Above the sky.",              "Cloud",      80, 89),
        new WorldDefinition("Mechanical", "⚙️", "Machines never sleep.",       "Mechanical", 90, 99),
        new WorldDefinition("Alien",      "👽", "Unknown life awaits.",        "Alien",      100, 109),
    };

    public static int Count => Worlds.Length;

    public static WorldDefinition GetByIndex(int worldIndex)
    {
        if (worldIndex < 0 || worldIndex >= Worlds.Length) return null;
        return Worlds[worldIndex];
    }

    public static WorldDefinition GetForScore(int score)
    {
        int index = PlanetSpawner.LevelIndexForScore(score);
        return GetByIndex(index);
    }

    public static int WorldIndexForScore(int score) => PlanetSpawner.LevelIndexForScore(score);

    // Score counts completed landings. The next spawned planet uses this score for its pool.
    public static int WorldIndexForNextPlanet(int landedScore) => WorldIndexForScore(landedScore);

    public static bool ShouldTransitionAfterLanding(int landedScore)
    {
        if (landedScore <= 0) return false;
        return landedScore % PlanetSpawner.PlanetsPerLevel == 0;
    }

    public static bool IsFirstPlanetOfWorld(int score)
    {
        if (score < 0) return false;
        return score % PlanetSpawner.PlanetsPerLevel == 0;
    }
}
