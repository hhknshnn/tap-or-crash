#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporary editor-only preview hook.
// Remove this file when "Restore production" is requested.
internal static class DesertDebugPreviewBootstrap
{
    const int DesertLevelIndex = 3;
    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= SelectDesertBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectDesertBeforeGameplayStart;
    }

    static void SelectDesertBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        activeGameManager.ScoreChanged -= PreserveDesertStart;
        activeGameManager.ScoreChanged += PreserveDesertStart;
        SetDesertStartScore();
    }

    static void PreserveDesertStart(int score)
    {
        if (score < DesertLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetDesertStartScore();
    }

    static void SetDesertStartScore()
    {
        int desertStartScore = DesertLevelIndex * PlanetSpawner.PlanetsPerLevel;
        ScoreField.SetValue(activeGameManager, desertStartScore);

        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = desertStartScore.ToString();
    }
}
#endif
