#if UNITY_EDITOR
using System.Reflection;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporary editor-only runtime preview. It does not touch PlayerPrefs or save data.
// Leave active until the user explicitly requests "Restore production".
internal static class OceanDebugPreviewBootstrap
{
    const int OceanLevelIndex = 4;
    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        // Follow the game's normal restart path so the preview enters gameplay
        // without mutating progression, save data or production scene wiring.
        GameManager.isRestart = true;
        SceneManager.sceneLoaded -= SelectOceanBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectOceanBeforeGameplayStart;
    }

    static void SelectOceanBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        GameManager.isRestart = true;
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        activeGameManager.ScoreChanged -= PreserveOceanStart;
        activeGameManager.ScoreChanged += PreserveOceanStart;
        SetOceanStartScore();

        GameObject runner = new GameObject("Ocean Debug Auto Start")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        runner.AddComponent<OceanDebugAutoStart>();
    }

    static void PreserveOceanStart(int score)
    {
        if (score < OceanLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetOceanStartScore();
    }

    static void SetOceanStartScore()
    {
        int oceanStartScore = OceanLevelIndex * PlanetSpawner.PlanetsPerLevel;
        ScoreField.SetValue(activeGameManager, oceanStartScore);

        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = oceanStartScore.ToString();
    }
}

internal sealed class OceanDebugAutoStart : MonoBehaviour
{
    IEnumerator Start()
    {
        // Run after the production GameManager.Start lifecycle has completed.
        yield return null;
        yield return null;

        GameManager manager = GameManager.instance;
        if (manager != null)
            manager.StartGame();
    }

    void Update()
    {
        if (!GameManager.isGameOver)
            GameManager.isGameStarted = true;
    }
}
#endif
