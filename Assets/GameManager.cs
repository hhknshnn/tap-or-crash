using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections; // Coroutine için

public class GameManager : MonoBehaviour
{
    // Singleton — tek bir GameManager olsun
    public static GameManager instance;

    // Oyun durumu
    public static bool isGameOver = false;
    private int score = 0;
    private int highScore = 0;  // En yüksek skor
    public TextMeshProUGUI highScoreText;  // Highscore yazısı
    public TextMeshProUGUI scoreResultText;

    // UI objeleri
    public TextMeshProUGUI scoreText;       // Skor yazısı
    public TextMeshProUGUI perfectText;     // "PERFECT!" yazısı
    public TextMeshProUGUI almostText;     // "ALMOST!" yazısı
    public GameObject gameOverPanel;        // Game Over ekranı

    // Coroutine referansı
    private Coroutine perfectCoroutine;

    void Awake()
    {
        // Singleton pattern
        instance = this;
        // Kaydedilmiş highscore'u yükle
        highScore = PlayerPrefs.GetInt("HighScore", 0);
    }

    // Her gezegen geçişinde skoru artır
    public void AddScore()
    {
        score++;
        scoreText.text = score.ToString();
        Debug.Log("AddScore çağrıldı: " + score);
    }

    public int GetScore()
    {
        return score;
    }
    // Perfect geçişte bonus puan ve yazı göster
    public void PerfectLaunch()
    {
        Debug.Log("PerfectLaunch çağrıldı!");
        score += 3;
        scoreText.text = score.ToString();

        // Eğer zaten gösteriliyorsa önce durdur
        if (perfectCoroutine != null)
            StopCoroutine(perfectCoroutine);

        // Perfect yazısını göster
        perfectCoroutine = StartCoroutine(ShowPerfectText());
    }

    // Perfect yazısını 1 saniye gösterip gizle
    IEnumerator ShowPerfectText()
    {
        Debug.Log("ShowPerfectText başladı!");
        perfectText.gameObject.SetActive(true);
        Debug.Log("PerfectText aktif edildi!");
        yield return new WaitForSeconds(1f);
        perfectText.gameObject.SetActive(false);
    }

    // Game Over'ı tetikle
    public void TriggerGameOver()
    {
        if (isGameOver) return;

        isGameOver = true;
        
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt("HighScore", highScore);
        }
        // Highscore yazısını güncelle
        highScoreText.text = "BEST: " + highScore;
        scoreResultText.text = "SCORE: " + score;
        GetComponent<RocketController>().enabled = false;

        // Slow motion ve Almost yazısı göster
        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        // ALMOST! yazısını göster
        almostText.gameObject.SetActive(true);

        // Slow motion başlat
        Time.timeScale = 0.2f;

        // 0.5 saniye bekle (slow motion'da)
        yield return new WaitForSecondsRealtime(0.8f);

        // Normal hıza dön
        Time.timeScale = 1f;

        // ALMOST! yazısını gizle
        almostText.gameObject.SetActive(false);

        // Game Over ekranını göster
        gameOverPanel.SetActive(true);
    }

    // Restart butonuna basınca sahneyi yeniden yükle
    public void RestartGame()
    {
        // Hızı sıfırla (slow motion kaldıysa)
        Time.timeScale = 1f;
        isGameOver = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}