#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporary editor-only runtime preview. It does not touch PlayerPrefs or save data.
// Leave active until the user explicitly requests "Restore production".
internal static class CloudDebugPreviewBootstrap
{
    const int CloudLevelIndex = 8;
    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        GameManager.isRestart = true;
        SceneManager.sceneLoaded -= SelectCloudBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectCloudBeforeGameplayStart;
    }

    static void SelectCloudBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        GameManager.isRestart = true;
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        activeGameManager.ScoreChanged -= PreserveCloudStart;
        activeGameManager.ScoreChanged += PreserveCloudStart;
        SetCloudStartScore();

        if (CloudDebugAutoStart.Active == null)
        {
            GameObject runner = new GameObject("Cloud Debug Auto Start")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            runner.AddComponent<CloudDebugAutoStart>();
        }
    }

    static void PreserveCloudStart(int score)
    {
        if (score < CloudLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetCloudStartScore();
    }

    internal static void SetCloudStartScore()
    {
        int cloudStartScore = CloudLevelIndex * PlanetSpawner.PlanetsPerLevel;
        ScoreField.SetValue(activeGameManager, cloudStartScore);

        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = cloudStartScore.ToString();
    }

    internal static void EnsureCloudStartScore()
    {
        GameManager current = GameManager.instance;
        if (current == null)
            current = Object.FindAnyObjectByType<GameManager>();
        if (current != null && activeGameManager != current)
            activeGameManager = current;

        if (activeGameManager != null
            && activeGameManager.GetScore() < CloudLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetCloudStartScore();
    }
}

[DefaultExecutionOrder(10003)]
internal sealed class CloudDebugAutoStart : MonoBehaviour
{
    internal static CloudDebugAutoStart Active { get; private set; }

    void Awake()
    {
        foreach (CloudDebugAutoStart other in Resources.FindObjectsOfTypeAll<CloudDebugAutoStart>())
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
                || typeName == "SakuraDebugAutoStart" || typeName == "MushroomDebugAutoStart")
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
        CloudDebugPreviewBootstrap.SetCloudStartScore();

        GameManager manager = GameManager.instance;
        if (manager != null)
            manager.StartGame();
    }

    void Update()
    {
        SuppressOlderReviewOverrides();
        CloudDebugPreviewBootstrap.EnsureCloudStartScore();
        if (!GameManager.isGameOver)
            GameManager.isGameStarted = true;
    }

    void LateUpdate()
    {
        CloudDebugPreviewBootstrap.EnsureCloudStartScore();
    }
}
#endif
