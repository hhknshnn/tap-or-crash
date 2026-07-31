using UnityEngine;

/// <summary>
/// The single authority for how asteroid pressure grows across a run.
///
/// Asteroids are silent until <see cref="ActivationPlanet"/>, then ease in from a
/// deliberately gentle introduction up to a hard ceiling. Every planet-number
/// decision about asteroid difficulty lives in <see cref="Curve"/> below and
/// nowhere else, so tuning the ramp never means hunting through spawner code.
/// </summary>
public static class AsteroidDifficulty
{
    /// <summary>Resolved difficulty for one moment in a run.</summary>
    public readonly struct Settings
    {
        /// <summary>Scales the spawner's already randomised asteroid speed.</summary>
        public readonly float SpeedMultiplier;

        /// <summary>Scales the spawner's base spawn interval. Larger means rarer.</summary>
        public readonly float SpawnIntervalMultiplier;

        public Settings(float speedMultiplier, float spawnIntervalMultiplier)
        {
            SpeedMultiplier = speedMultiplier;
            SpawnIntervalMultiplier = spawnIntervalMultiplier;
        }
    }

    private readonly struct ControlPoint
    {
        public readonly int Planet;
        public readonly float SpeedMultiplier;
        public readonly float SpawnIntervalMultiplier;

        public ControlPoint(int planet, float speedMultiplier, float spawnIntervalMultiplier)
        {
            Planet = planet;
            SpeedMultiplier = speedMultiplier;
            SpawnIntervalMultiplier = spawnIntervalMultiplier;
        }

        public Settings ToSettings() => new Settings(SpeedMultiplier, SpawnIntervalMultiplier);
    }

    // ─── Tuning table — the only place these numbers live ────────────────────
    //
    // Must stay ordered by ascending planet. The first row doubles as the
    // activation threshold; the last row doubles as the permanent ceiling.
    private static readonly ControlPoint[] Curve =
    {
        new ControlPoint(21, 0.60f, 1.50f), // introduction: slow and sparse
        new ControlPoint(40, 1.00f, 1.00f), // full strength
        new ControlPoint(80, 1.25f, 0.85f), // ceiling, held for every later planet
    };

    /// <summary>First planet on which asteroids may spawn.</summary>
    public static int ActivationPlanet => Curve[0].Planet;

    /// <summary>
    /// Score is the count of completed landings, so the planet the rocket is
    /// currently flying towards is always one ahead of it.
    /// </summary>
    public static int PlanetForScore(int score) => score + 1;

    /// <summary>True once the run has reached <see cref="ActivationPlanet"/>.</summary>
    public static bool IsActive(int score) => PlanetForScore(score) >= ActivationPlanet;

    public static Settings EvaluateForScore(int score) => Evaluate(PlanetForScore(score));

    /// <summary>
    /// Smoothly interpolates the curve. Each segment eases in and out, so the
    /// value is flat at every control point and the ramp never steps at a
    /// segment boundary. Outside the table the nearest end point is held.
    /// </summary>
    public static Settings Evaluate(int planet)
    {
        ControlPoint first = Curve[0];
        if (planet <= first.Planet) return first.ToSettings();

        ControlPoint last = Curve[Curve.Length - 1];
        if (planet >= last.Planet) return last.ToSettings();

        for (int i = 1; i < Curve.Length; i++)
        {
            ControlPoint end = Curve[i];
            if (planet > end.Planet) continue;

            ControlPoint start = Curve[i - 1];
            float t = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start.Planet, end.Planet, planet));

            return new Settings(
                Mathf.Lerp(start.SpeedMultiplier, end.SpeedMultiplier, t),
                Mathf.Lerp(start.SpawnIntervalMultiplier, end.SpawnIntervalMultiplier, t));
        }

        return last.ToSettings();
    }
}
