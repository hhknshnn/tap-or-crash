using System;
using UnityEngine;

// Decides which planet theme the main menu shows off.
//
// No theme name is ever written here. The world list is read from PlanetSpawner.levels
// and the accent colour from the PlanetAmbience registry, so a world added to the
// spawner (or a new PlanetAmbience subclass) shows up in the menu with no code change.
//
// Selection order:
//   1. PlayerPrefs[SelectedThemeKey] — a future theme picker only has to write this key
//   2. the furthest world the player has reached, derived from their best score
public static class MenuShowcaseTheme
{
    // Write a level name here (e.g. "Ice") to pin the menu to one theme.
    public const string SelectedThemeKey = "SelectedPlanetTheme";

    const string BestScoreKey = "HighScore";

    static readonly Color FallbackAccent = new Color(0.34f, 0.86f, 1f, 1f);

    public struct Selection
    {
        public string themeName;
        public GameObject prefab;
        public Color accent;

        public bool IsValid => prefab != null;
    }

    public static Selection Resolve(PlanetSpawner spawner)
    {
        Selection selection = new Selection { accent = FallbackAccent };
        if (spawner == null) return selection;

        PlanetSpawner.PlanetLevel level = PickLevel(spawner);
        GameObject[] pool = level != null && HasEntry(level.prefabs) ? level.prefabs : spawner.planetPrefabs;

        selection.prefab = PickPrefab(pool);
        if (selection.prefab == null) return selection;

        selection.themeName = level != null ? level.levelName : null;
        selection.accent = PlanetAmbience.AccentColorFor(selection.themeName, FallbackAccent);
        selection.accent.a = 1f;
        return selection;
    }

    static PlanetSpawner.PlanetLevel PickLevel(PlanetSpawner spawner)
    {
        PlanetSpawner.PlanetLevel[] levels = spawner.levels;
        if (levels == null || levels.Length == 0) return null;

        string requested = PlayerPrefs.GetString(SelectedThemeKey, string.Empty).Trim();
        if (requested.Length > 0)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != null
                    && string.Equals(levels[i].levelName, requested, StringComparison.OrdinalIgnoreCase))
                    return levels[i];
            }
        }

        // No explicit choice: show the world the player has actually flown to. Landing
        // in a new world is what changes the menu, which makes progress visible.
        int reached = PlanetSpawner.LevelIndexForScore(PlayerPrefs.GetInt(BestScoreKey, 0));
        return levels[Mathf.Clamp(reached, 0, levels.Length - 1)];
    }

    // A different planet of the same world each time the menu opens, so the showcase
    // never looks like a static screenshot across sessions.
    static GameObject PickPrefab(GameObject[] pool)
    {
        if (!HasEntry(pool)) return null;

        int start = UnityEngine.Random.Range(0, pool.Length);
        for (int i = 0; i < pool.Length; i++)
        {
            GameObject candidate = pool[(start + i) % pool.Length];
            if (candidate != null) return candidate;
        }
        return null;
    }

    static bool HasEntry(GameObject[] pool)
    {
        if (pool == null) return false;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] != null) return true;
        return false;
    }
}
