#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor-only preview switch: starts Play Mode directly inside one themed level.
// It does not touch PlayerPrefs or save data, and it compiles out of builds.
//
// This replaces the per-biome bootstraps (Desert/Ocean/Crystal/Sakura/Mushroom/
// Cloud), which each registered their own scene-loaded handler and fought over
// the starting score — whichever ran first decided the two planets that
// PlanetSpawner.Start() spawned, so the preview could open on the wrong level.
//
// To preview a different level, set PreviewLevel. Set it to null or empty to
// disable the preview entirely and boot the game normally.
internal static class LevelPreviewBootstrap
{
    // The level to open on. Must match a levelName in the scene's PlanetSpawner.
    const string PreviewLevel = "Mechanical";

    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;
    static int previewScore = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        if (string.IsNullOrEmpty(PreviewLevel)) return;

        GameManager.isRestart = true;
        SceneManager.sceneLoaded -= SelectLevelBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectLevelBeforeGameplayStart;
    }

    // sceneLoaded runs after every Awake and before any Start, which is the only
    // window where the score can be set before PlanetSpawner spawns its first
    // two planets.
    static void SelectLevelBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        GameManager.isRestart = true;
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        previewScore = ResolvePreviewScore();
        if (previewScore < 0)
        {
            Debug.LogWarning($"LevelPreviewBootstrap: no level named '{PreviewLevel}' "
                             + "in the scene's PlanetSpawner. Preview disabled.");
            return;
        }

        activeGameManager.ScoreChanged -= PreservePreviewStart;
        activeGameManager.ScoreChanged += PreservePreviewStart;
        ApplyPreviewScore();

        if (LevelPreviewAutoStart.Active == null)
        {
            GameObject runner = new GameObject("Level Preview Auto Start")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            runner.AddComponent<LevelPreviewAutoStart>();
        }
    }

    static int ResolvePreviewScore()
    {
        PlanetSpawner spawner = Object.FindAnyObjectByType<PlanetSpawner>();
        if (spawner == null || spawner.levels == null) return -1;

        for (int i = 0; i < spawner.levels.Length; i++)
        {
            if (spawner.levels[i] != null
                && string.Equals(spawner.levels[i].levelName, PreviewLevel,
                    System.StringComparison.OrdinalIgnoreCase))
                return i * PlanetSpawner.PlanetsPerLevel;
        }
        return -1;
    }

    static void PreservePreviewStart(int score)
    {
        if (score < previewScore) ApplyPreviewScore();
    }

    static void ApplyPreviewScore()
    {
        if (activeGameManager == null || previewScore < 0) return;

        ScoreField.SetValue(activeGameManager, previewScore);
        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = previewScore.ToString();
    }

    internal static void EnsurePreviewScore()
    {
        if (previewScore < 0) return;

        GameManager current = GameManager.instance;
        if (current == null)
            current = Object.FindAnyObjectByType<GameManager>();
        if (current != null && activeGameManager != current)
            activeGameManager = current;

        if (activeGameManager != null && activeGameManager.GetScore() < previewScore)
            ApplyPreviewScore();
    }
}

[DefaultExecutionOrder(10000)]
internal sealed class LevelPreviewAutoStart : MonoBehaviour
{
    internal static LevelPreviewAutoStart Active { get; private set; }

    void Awake()
    {
        foreach (LevelPreviewAutoStart other in Resources.FindObjectsOfTypeAll<LevelPreviewAutoStart>())
        {
            if (other != null && other != this)
                DestroyImmediate(other.gameObject);
        }

        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    IEnumerator Start()
    {
        yield return null;
        yield return null;

        LevelPreviewBootstrap.EnsurePreviewScore();

        GameManager manager = GameManager.instance;
        if (manager != null)
            manager.StartGame();
    }

    void Update()
    {
        LevelPreviewBootstrap.EnsurePreviewScore();
        if (!GameManager.isGameOver)
            GameManager.isGameStarted = true;
    }

    void LateUpdate()
    {
        LevelPreviewBootstrap.EnsurePreviewScore();
    }
}
#endif
