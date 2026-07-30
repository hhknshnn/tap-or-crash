#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporary editor-only runtime preview. It does not touch PlayerPrefs or save data.
// Leave active until the user explicitly requests "Restore production".
internal static class SakuraDebugPreviewBootstrap
{
    const int SakuraLevelIndex = 6;
    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        GameManager.isRestart = true;
        SceneManager.sceneLoaded -= SelectSakuraBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectSakuraBeforeGameplayStart;
    }

    static void SelectSakuraBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        GameManager.isRestart = true;
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        activeGameManager.ScoreChanged -= PreserveSakuraStart;
        activeGameManager.ScoreChanged += PreserveSakuraStart;
        SetSakuraStartScore();

        if (SakuraDebugAutoStart.Active == null)
        {
            GameObject runner = new GameObject("Sakura Debug Auto Start")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            runner.AddComponent<SakuraDebugAutoStart>();
        }
    }

    static void PreserveSakuraStart(int score)
    {
        if (score < SakuraLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetSakuraStartScore();
    }

    internal static void SetSakuraStartScore()
    {
        int sakuraStartScore = SakuraLevelIndex * PlanetSpawner.PlanetsPerLevel;
        ScoreField.SetValue(activeGameManager, sakuraStartScore);

        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = sakuraStartScore.ToString();
    }

    internal static void EnsureSakuraStartScore()
    {
        GameManager current = GameManager.instance;
        if (current == null)
            current = Object.FindAnyObjectByType<GameManager>();
        if (current != null && activeGameManager != current)
            activeGameManager = current;

        if (activeGameManager != null
            && activeGameManager.GetScore() < SakuraLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetSakuraStartScore();
    }
}

[DefaultExecutionOrder(10001)]
internal sealed class SakuraDebugAutoStart : MonoBehaviour
{
    internal static SakuraDebugAutoStart Active { get; private set; }

    void Awake()
    {
        foreach (SakuraDebugAutoStart other in Resources.FindObjectsOfTypeAll<SakuraDebugAutoStart>())
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

    // The approved Ocean and Crystal review overrides stay as locked assets; only
    // their transient runtime runners are suppressed so they cannot pull this
    // review back to their own level.
    static void SuppressOlderReviewOverrides()
    {
        foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "OceanDebugAutoStart" || typeName == "CrystalDebugAutoStart")
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
        SakuraDebugPreviewBootstrap.SetSakuraStartScore();

        GameManager manager = GameManager.instance;
        if (manager != null)
            manager.StartGame();
    }

    void Update()
    {
        SuppressOlderReviewOverrides();
        SakuraDebugPreviewBootstrap.EnsureSakuraStartScore();
        if (!GameManager.isGameOver)
            GameManager.isGameStarted = true;
    }

    void LateUpdate()
    {
        SakuraDebugPreviewBootstrap.EnsureSakuraStartScore();
    }
}
#endif
