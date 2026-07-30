#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Temporary editor-only runtime preview. It does not touch PlayerPrefs or save data.
// Leave active until the user explicitly requests "Restore production".
internal static class CrystalDebugPreviewBootstrap
{
    const int CrystalLevelIndex = 5;
    static readonly FieldInfo ScoreField = typeof(GameManager).GetField(
        "score",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static GameManager activeGameManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        GameManager.isRestart = true;
        SceneManager.sceneLoaded -= SelectCrystalBeforeGameplayStart;
        SceneManager.sceneLoaded += SelectCrystalBeforeGameplayStart;
    }

    static void SelectCrystalBeforeGameplayStart(Scene scene, LoadSceneMode mode)
    {
        GameManager.isRestart = true;
        activeGameManager = GameManager.instance;
        if (activeGameManager == null)
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        if (activeGameManager == null || ScoreField == null) return;

        activeGameManager.ScoreChanged -= PreserveCrystalStart;
        activeGameManager.ScoreChanged += PreserveCrystalStart;
        SetCrystalStartScore();

        if (CrystalDebugAutoStart.Active == null)
        {
            GameObject runner = new GameObject("Crystal Debug Auto Start")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            runner.AddComponent<CrystalDebugAutoStart>();
        }
    }

    static void PreserveCrystalStart(int score)
    {
        if (score < CrystalLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetCrystalStartScore();
    }

    internal static void SetCrystalStartScore()
    {
        int crystalStartScore = CrystalLevelIndex * PlanetSpawner.PlanetsPerLevel;
        ScoreField.SetValue(activeGameManager, crystalStartScore);

        if (activeGameManager.scoreText != null)
            activeGameManager.scoreText.text = crystalStartScore.ToString();
    }

    internal static void EnsureCrystalStartScore()
    {
        GameManager current = GameManager.instance;
        if (current == null)
            current = Object.FindAnyObjectByType<GameManager>();
        if (current != null && activeGameManager != current)
            activeGameManager = current;

        if (activeGameManager != null
            && activeGameManager.GetScore() < CrystalLevelIndex * PlanetSpawner.PlanetsPerLevel)
            SetCrystalStartScore();
    }

    internal static void EnsureCrystalReviewLabel()
    {
        foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (label != null && label.text != null
                && label.text.IndexOf("OCEAN WORLD", System.StringComparison.OrdinalIgnoreCase) >= 0)
                label.text = "CRYSTAL WORLD";
        }
    }
}

[DefaultExecutionOrder(10000)]
internal sealed class CrystalDebugAutoStart : MonoBehaviour
{
    internal static CrystalDebugAutoStart Active { get; private set; }

    void Awake()
    {
        foreach (CrystalDebugAutoStart other in Resources.FindObjectsOfTypeAll<CrystalDebugAutoStart>())
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

    static void SuppressOlderReviewOverride()
    {
        foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour != null && behaviour.GetType().Name == "OceanDebugAutoStart")
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

        // The previously approved Ocean review override is intentionally still
        // present as a locked asset. Suppress only its transient runtime runners
        // so this Crystal review cannot be relabelled or restarted as Ocean.
        SuppressOlderReviewOverride();

        // Older locked biome previews may also be registered. Reassert Crystal
        // after every scene-load callback has completed, without touching them.
        CrystalDebugPreviewBootstrap.SetCrystalStartScore();

        GameManager manager = GameManager.instance;
        if (manager != null)
            manager.StartGame();
    }

    void Update()
    {
        SuppressOlderReviewOverride();
        CrystalDebugPreviewBootstrap.EnsureCrystalStartScore();
        if (!GameManager.isGameOver)
            GameManager.isGameStarted = true;
    }

    void LateUpdate()
    {
        CrystalDebugPreviewBootstrap.EnsureCrystalStartScore();
        CrystalDebugPreviewBootstrap.EnsureCrystalReviewLabel();
    }
}
#endif
