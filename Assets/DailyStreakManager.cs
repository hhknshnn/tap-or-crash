using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

// Günlük oturum takibi ve streak ödülleri.
// Otomatik başlatılır — sahneye elle eklenmesi gerekmez.
public class DailyStreakManager : MonoBehaviour
{
    public static DailyStreakManager instance;

    private const string LastLoginKey = "LastLoginDate";
    private const string StreakKey    = "DailyStreak";

    // Singleton'ı sahne yüklendikten sonra otomatik oluştur
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInit()
    {
        if (instance != null) return;
        GameObject go = new GameObject("DailyStreakManager");
        DontDestroyOnLoad(go);
        go.AddComponent<DailyStreakManager>();
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        CheckDailyLogin();
    }

    void CheckDailyLogin()
    {
        string today     = DateTime.Now.ToString("yyyy-MM-dd");
        string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
        string lastLogin = PlayerPrefs.GetString(LastLoginKey, "");

        if (lastLogin == today) return; // Bugün zaten giriş yapıldı

        int streak = PlayerPrefs.GetInt(StreakKey, 0);

        if (lastLogin == yesterday)
            streak++;       // Arka arkaya gün — seri devam
        else
            streak = 1;     // Kaçırıldı veya ilk giriş — sıfırla

        PlayerPrefs.SetString(LastLoginKey, today);
        PlayerPrefs.SetInt(StreakKey, streak);
        PlayerPrefs.Save();

        // Streak tracking remains, but daily login no longer grants coins.
    }

    // CoinManager Start()'tan sonra başlatılabilmesi için bir frame bekle
    IEnumerator GiveRewardNextFrame(int streak, int coinReward)
    {
        yield return new WaitForSeconds(0.5f); // UI tam hazır olsun
        StartCoroutine(ShowStreakBanner(streak, coinReward));
    }

    IEnumerator ShowStreakBanner(int streak, int coins)
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) yield break;

        // Banner oluştur
        GameObject go = new GameObject("StreakBanner");
        go.transform.SetParent(canvas.transform, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 1f);
        rt.anchorMax       = new Vector2(0.5f, 1f);
        rt.pivot           = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -5f);
        rt.sizeDelta       = new Vector2(300f, 65f);

        Image bg = go.AddComponent<Image>();
        UIStyleKit.ApplyPanel(bg, UIStyleKit.BgCard);

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        RectTransform textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 0f);
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = $"DAY {streak} STREAK\n+{coins} COINS";
        tmp.fontSize  = 18;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(1f, 0.85f, 0.1f);
        tmp.fontStyle = FontStyles.Bold;

        CanvasGroup cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // Aşağıya kayarak açıl
        Vector2 hiddenPos = new Vector2(0f, -5f);
        Vector2 shownPos  = new Vector2(0f, -5f);
        rt.anchoredPosition = hiddenPos;

        float t = 0f;
        while (t < 0.35f)
        {
            t    += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / 0.35f);
            cg.alpha            = p;
            rt.anchoredPosition = Vector2.Lerp(new Vector2(0f, 30f), shownPos, p);
            yield return null;
        }
        cg.alpha = 1f;
        rt.anchoredPosition = shownPos;

        yield return new WaitForSeconds(2.5f);

        // Yukarı kayarak kapat
        t = 0f;
        while (t < 0.3f)
        {
            t    += Time.deltaTime;
            float p = t / 0.3f;
            cg.alpha            = 1f - p;
            rt.anchoredPosition = Vector2.Lerp(shownPos, new Vector2(0f, 30f), p);
            yield return null;
        }
        Destroy(go);
    }

    public int GetStreak() => PlayerPrefs.GetInt(StreakKey, 0);
}
