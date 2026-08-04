using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    public static PauseManager instance;

    [Header("UI")]
    public GameObject pausePanel;       // PausePanel'i Inspector'dan ata
    public GameObject gameUI;           // GameUI'ı Inspector'dan ata — pause'da gizlenecek

    private bool isPaused = false;
    private bool pauseQueuedDuringTransition = false;
    public bool IsPaused => isPaused;

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        // Başlangıçta panel kapalı
        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    void Update()
    {
        // Oyun başlamadıysa veya bittiyse pause butonunu gizle
        if (gameUI != null)
        {
            bool showUI = GameManager.isGameStarted
                && (!GameManager.isGameOver || GameManager.IsAlmostFeedbackPlaying)
                && !isPaused;
            gameUI.SetActive(showUI);
        }
    }

    // PauseButton'a bağlanacak
    public void TogglePause()
    {
        if (GameManager.isGameOver) return; // Game over'da pause olmaz

        if (WorldTransitionManager.IsPlaying)
        {
            pauseQueuedDuringTransition = true;
            return;
        }

        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void ExecuteQueuedPause()
    {
        if (!pauseQueuedDuringTransition || GameManager.isGameOver) return;
        pauseQueuedDuringTransition = false;
        PauseGame();
    }

    public void PauseGame()
    {
        if (WorldTransitionManager.IsPlaying)
        {
            pauseQueuedDuringTransition = true;
            return;
        }

        CancelRocketInput();
        isPaused = true;
        PresentationGate.Acquire(PresentationGate.Kind.Pause);
        Time.timeScale = 0f;                    // Oyunu dondur
        if (pausePanel != null)
            pausePanel.SetActive(true);
    }

    public void ResumeGame()
    {
        CancelRocketInput();
        isPaused = false;
        PresentationGate.Release(PresentationGate.Kind.Pause);
        Time.timeScale = 1f;                    // Oyunu devam ettir
        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    // MainMenuButton'a bağlanacak
    public void GoToMainMenu()
    {
        CancelRocketInput();
        isPaused = false;
        PresentationGate.Release(PresentationGate.Kind.Pause);
        Time.timeScale = 1f;                    // timeScale'i sıfırla
        GameManager.isGameOver = false;         // Static değişkenleri temizle
        GameManager.isGameStarted = false;
        GameManager.isRestart = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); // Sahneyi yeniden yükle
    }

    void CancelRocketInput()
    {
        RocketController rocket = GameManager.instance != null
            ? GameManager.instance.playerRocket
            : null;
        if (rocket == null) rocket = FindAnyObjectByType<RocketController>();
        if (rocket != null) rocket.CancelHoldInput();
    }
}
