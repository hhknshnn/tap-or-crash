using UnityEngine;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using System;

public class AdManager : MonoBehaviour
{
    public static AdManager instance;

    // Gerçek AdMob reklam birimi ID'leri (production)
    private const string InterstitialAdUnitId = "ca-app-pub-9310700764525340/9936988396"; // GameOver_Interstitial
    private const string RewardedAdUnitId     = "ca-app-pub-9310700764525340/8305444545"; // GameOver_Rewarded

    private InterstitialAd interstitialAd;
    private RewardedAd     rewardedAd;
    private int            gameOverCount = 0;

    // Rewarded ad ödüllendirilince çağrılacak callback
    private Action<int>    pendingRewardCallback;
    private int            pendingRewardAmount;
    private bool           rewardedAdShowing;

    private static bool AdsSupported =>
        Application.platform == RuntimePlatform.Android ||
        Application.platform == RuntimePlatform.IPhonePlayer;

    void Awake()
    {
        if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // Google Mobile Ads uses placeholder clients in the Unity Editor and on
        // unsupported desktop platforms. Only initialize the real mobile SDK.
        if (!AdsSupported) return;

        var requestParameters = new ConsentRequestParameters();

        ConsentInformation.Update(requestParameters, (FormError updateError) =>
        {
            if (updateError != null)
            {
                Debug.LogError("Consent bilgisi güncellenemedi: " + updateError.Message);
                InitializeAds();
                return;
            }

            ConsentForm.LoadAndShowConsentFormIfRequired((FormError formError) =>
            {
                if (formError != null)
                {
                    Debug.LogError("Consent formu hatası: " + formError.Message);
                }

                if (ConsentInformation.CanRequestAds())
                {
                    InitializeAds();
                }
            });
        });
    }

    void InitializeAds()
    {
        MobileAds.Initialize(_ =>
        {
            LoadInterstitialAd();
            LoadRewardedAd();
        });
    }

    // ─── Interstitial ─────────────────────────────────────────────────────────

    void LoadInterstitialAd()
    {
        interstitialAd?.Destroy();
        interstitialAd = null;

        InterstitialAd.Load(InterstitialAdUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null) { Debug.LogError("Interstitial yüklenemedi: " + error); return; }
            interstitialAd = ad;
        });
    }

    public void OnGameOver()
    {
        if (!AdsSupported) return;

        gameOverCount++;
        if (gameOverCount % 3 == 0) ShowInterstitialAd();
    }

    void ShowInterstitialAd()
    {
        if (interstitialAd != null && interstitialAd.CanShowAd())
        {
            // Önce kapanma olayına abone ol, sonra reklamı göster (doğru sıra)
            interstitialAd.OnAdFullScreenContentClosed += OnInterstitialClosed;
            interstitialAd.Show();
        }
        else { LoadInterstitialAd(); }
    }

    void OnInterstitialClosed()
    {
        // Aboneliği kaldır ve bir sonraki gösterim için yeni reklam yükle
        interstitialAd.OnAdFullScreenContentClosed -= OnInterstitialClosed;
        LoadInterstitialAd();
    }

    // ─── Rewarded Ad ──────────────────────────────────────────────────────────

    void LoadRewardedAd()
    {
        rewardedAd?.Destroy();
        rewardedAd = null;

        RewardedAd.Load(RewardedAdUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null) { Debug.LogError("Rewarded reklam yüklenemedi: " + error); return; }
            rewardedAd = ad;
        });
    }

    // Reklam izlenince verilecek coin miktarı ve callback
    public bool IsRewardedAdReady() =>
        AdsSupported && rewardedAd != null && rewardedAd.CanShowAd();

    public void ShowRewardedAdForCoins(Action<int> onRewarded = null)
    {
        if (!AdsSupported || rewardedAdShowing) return;

        if (rewardedAd == null || !rewardedAd.CanShowAd())
        {
            Debug.Log("Rewarded reklam hazır değil, yeniden yükleniyor.");
            LoadRewardedAd();
            return;
        }

        pendingRewardAmount   = GameEconomyConfig.Current.rewardedAdCoins;
        pendingRewardCallback = onRewarded;
        bool rewardGranted = false;
        rewardedAdShowing = true;

        rewardedAd.OnAdFullScreenContentClosed += OnRewardedClosed;
        rewardedAd.OnAdFullScreenContentFailed += OnRewardedFailed;
        rewardedAd.Show(reward =>
        {
            if (rewardGranted) return;
            rewardGranted = true;
            // Kullanıcı reklamı izledi — ödülü ver
            if (CoinManager.instance != null) CoinManager.instance.AddCoins(pendingRewardAmount);
            pendingRewardCallback?.Invoke(pendingRewardAmount);
            pendingRewardCallback = null;
        });
    }

    void OnRewardedClosed()
    {
        rewardedAd.OnAdFullScreenContentClosed -= OnRewardedClosed;
        rewardedAd.OnAdFullScreenContentFailed -= OnRewardedFailed;
        rewardedAdShowing = false;
        LoadRewardedAd(); // Bir sonraki gösterim için yükle
    }

    void OnRewardedFailed(AdError error)
    {
        rewardedAd.OnAdFullScreenContentClosed -= OnRewardedClosed;
        rewardedAd.OnAdFullScreenContentFailed -= OnRewardedFailed;
        rewardedAdShowing = false;
        pendingRewardCallback = null;
        LoadRewardedAd();
    }
}
