#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporary editor-only runtime preview. It does not touch PlayerPrefs or save data.
// Leave active until the user explicitly requests "Restore production".
internal static class MushroomDebugPreviewBootstrap
{
    const int MushroomLevelIndex = 7;
    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        GameManager.isRestart = true;
        SceneManager.sceneLoaded -= SelectMushroomBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectMushroomBeforeGameplayStart;
    }

    static void SelectMushroomBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        GameManager.isRestart = true;
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        activeGameManager.ScoreChanged -= PreserveMushroomStart;
        activeGameManager.ScoreChanged += PreserveMushroomStart;
        SetMushroomStartScore();

        if (MushroomDebugAutoStart.Active == null)
        {
            GameObject runner = new GameObject("Mushroom Debug Auto Start")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            runner.AddComponent<MushroomDebugAutoStart>();
        }
    }

    static void PreserveMushroomStart(int score)
    {
        if (score < MushroomLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetMushroomStartScore();
    }

    internal static void SetMushroomStartScore()
    {
        int mushroomStartScore = MushroomLevelIndex * PlanetSpawner.PlanetsPerLevel;
        ScoreField.SetValue(activeGameManager, mushroomStartScore);

        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = mushroomStartScore.ToString();
    }

    internal static void EnsureMushroomStartScore()
    {
        GameManager current = GameManager.instance;
        if (current == null)
            current = Object.FindAnyObjectByType<GameManager>();
        if (current != null && activeGameManager != current)
            activeGameManager = current;

        if (activeGameManager != null
            && activeGameManager.GetScore() < MushroomLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetMushroomStartScore();
    }
}

[DefaultExecutionOrder(10002)]
internal sealed class MushroomDebugAutoStart : MonoBehaviour
{
    internal static MushroomDebugAutoStart Active { get; private set; }

    void Awake()
    {
        foreach (MushroomDebugAutoStart other in Resources.FindObjectsOfTypeAll<MushroomDebugAutoStart>())
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

    // Earlier approved biome previews stay as locked assets; only their transient
    // runtime runners are suppressed so they cannot pull this review to their level.
    static void SuppressOlderReviewOverrides()
    {
        foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "OceanDebugAutoStart" || typeName == "CrystalDebugAutoStart"
                || typeName == "SakuraDebugAutoStart")
            {
                behaviour.StopAllCoroutines();
                behaviour.enabled = false;
            }
        }
    }

    IEnumerator Start()
    {
        yield return null;
        yield return null;

        SuppressOlderReviewOverrides();
        MushroomDebugPreviewBootstrap.SetMushroomStartScore();

        GameManager manager = GameManager.instance;
        if (manager != null)
            manager.StartGame();
    }

    void Update()
    {
        SuppressOlderReviewOverrides();
        MushroomDebugPreviewBootstrap.EnsureMushroomStartScore();
        if (!GameManager.isGameOver)
            GameManager.isGameStarted = true;
    }

    void LateUpdate()
    {
        MushroomDebugPreviewBootstrap.EnsureMushroomStartScore();
    }
}
#endif
