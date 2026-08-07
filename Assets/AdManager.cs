using System;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

public class AdManager : MonoBehaviour
{
    public static AdManager instance;

#if UNITY_ANDROID
    // Android production reklam birimleri
    private const string InterstitialAdUnitId =
        "ca-app-pub-9310700764525340/9936988396";

    private const string RewardedAdUnitId =
        "ca-app-pub-9310700764525340/8305444545";

#elif UNITY_IOS
    // iOS production reklam birimleri
    private const string InterstitialAdUnitId =
        "ca-app-pub-9310700764525340/1117245310";

    private const string RewardedAdUnitId =
        "ca-app-pub-9310700764525340/5198980619";

#else
    // Unity Editor ve desteklenmeyen platformlarda kullanılmaz.
    private const string InterstitialAdUnitId = "unused";
    private const string RewardedAdUnitId = "unused";
#endif

    private InterstitialAd interstitialAd;
    private RewardedAd rewardedAd;

    private int gameOverCount;
    private Action<int> pendingRewardCallback;
    private int pendingRewardAmount;
    private Action pendingGenericGrantedCallback;
    private Action pendingGenericClosedCallback;
    private bool rewardedAdShowing;
    private bool adsInitialized;

    private static bool AdsSupported =>
        Application.platform == RuntimePlatform.Android ||
        Application.platform == RuntimePlatform.IPhonePlayer;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            return;
        }

        Destroy(gameObject);
    }

    private void Start()
    {
        if (!AdsSupported)
        {
            Debug.Log("AdMob initialization skipped on unsupported platform.");
            return;
        }

        RequestConsentAndInitializeAds();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        DestroyInterstitialAd();
        DestroyRewardedAd();
    }

    private void RequestConsentAndInitializeAds()
    {
        ConsentRequestParameters requestParameters =
            new ConsentRequestParameters();

        ConsentInformation.Update(requestParameters, updateError =>
        {
            if (updateError != null)
            {
                Debug.LogError(
                    "Consent bilgisi güncellenemedi: " +
                    updateError.Message
                );

                InitializeAds();
                return;
            }

            ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
            {
                if (formError != null)
                {
                    Debug.LogError(
                        "Consent formu hatası: " +
                        formError.Message
                    );
                }

                if (ConsentInformation.CanRequestAds())
                {
                    InitializeAds();
                }
                else
                {
                    Debug.LogWarning(
                        "Kullanıcı izni nedeniyle şu anda reklam isteği gönderilemiyor."
                    );
                }
            });
        });
    }

    private void InitializeAds()
    {
        if (adsInitialized)
        {
            return;
        }

        adsInitialized = true;

        MobileAds.Initialize(initializationStatus =>
        {
            Debug.Log("Google Mobile Ads initialized.");

            LoadInterstitialAd();
            LoadRewardedAd();
        });
    }

    // ─── Interstitial ──────────────────────────────────────────────────────

    private void LoadInterstitialAd()
    {
        if (!AdsSupported || !adsInitialized)
        {
            return;
        }

        DestroyInterstitialAd();

        AdRequest request = new AdRequest();

        InterstitialAd.Load(
            InterstitialAdUnitId,
            request,
            (ad, error) =>
            {
                if (error != null)
                {
                    Debug.LogError(
                        "Interstitial yüklenemedi: " +
                        error
                    );
                    return;
                }

                if (ad == null)
                {
                    Debug.LogError(
                        "Interstitial yükleme callback'i boş reklam döndürdü."
                    );
                    return;
                }

                interstitialAd = ad;

                Debug.Log("Interstitial reklam yüklendi.");
            }
        );
    }

    public void OnGameOver()
    {
        if (!AdsSupported)
        {
            return;
        }

        gameOverCount++;

        if (gameOverCount % 3 == 0)
        {
            ShowInterstitialAd();
        }
    }

    private void ShowInterstitialAd()
    {
        // Covers gameplay, Shop, Fuel popup, Continue, Game Over, a rewarded ad
        // already on screen and an active IAP purchase sheet in one check: all
        // of them hold a PresentationGate kind for exactly as long as they own
        // the screen. This cycle is simply skipped rather than queued — the ad
        // stays loaded and the next OnGameOver() call tries again.
        if (PresentationGate.IsAnyFullScreenPresentationActive)
        {
            Debug.Log("Interstitial ertelendi: başka bir sunum aktif.");
            return;
        }

        if (interstitialAd == null || !interstitialAd.CanShowAd())
        {
            Debug.Log("Interstitial hazır değil, yeniden yükleniyor.");
            LoadInterstitialAd();
            return;
        }

        interstitialAd.OnAdFullScreenContentClosed +=
            OnInterstitialClosed;

        interstitialAd.OnAdFullScreenContentFailed +=
            OnInterstitialFailed;

        PresentationGate.AcquireAdvertisement();
        interstitialAd.Show();
    }

    private void OnInterstitialClosed()
    {
        PresentationGate.ReleaseAdvertisement();
        UnsubscribeInterstitialEvents();
        LoadInterstitialAd();
    }

    private void OnInterstitialFailed(AdError error)
    {
        PresentationGate.ReleaseAdvertisement();
        Debug.LogError(
            "Interstitial gösterilemedi: " +
            error
        );

        UnsubscribeInterstitialEvents();
        LoadInterstitialAd();
    }

    private void UnsubscribeInterstitialEvents()
    {
        if (interstitialAd == null)
        {
            return;
        }

        interstitialAd.OnAdFullScreenContentClosed -=
            OnInterstitialClosed;

        interstitialAd.OnAdFullScreenContentFailed -=
            OnInterstitialFailed;
    }

    private void DestroyInterstitialAd()
    {
        if (interstitialAd == null)
        {
            return;
        }

        UnsubscribeInterstitialEvents();
        interstitialAd.Destroy();
        interstitialAd = null;
    }

    // ─── Rewarded ──────────────────────────────────────────────────────────

    private void LoadRewardedAd()
    {
        if (!AdsSupported || !adsInitialized)
        {
            return;
        }

        DestroyRewardedAd();

        AdRequest request = new AdRequest();

        RewardedAd.Load(
            RewardedAdUnitId,
            request,
            (ad, error) =>
            {
                if (error != null)
                {
                    Debug.LogError(
                        "Rewarded reklam yüklenemedi: " +
                        error
                    );
                    return;
                }

                if (ad == null)
                {
                    Debug.LogError(
                        "Rewarded yükleme callback'i boş reklam döndürdü."
                    );
                    return;
                }

                rewardedAd = ad;

                Debug.Log("Rewarded reklam yüklendi.");
            }
        );
    }

    public bool IsRewardedAdReady()
    {
        return AdsSupported &&
               rewardedAd != null &&
               rewardedAd.CanShowAd() &&
               !rewardedAdShowing;
    }

    public void ShowRewardedAdForCoins(
        Action<int> onRewarded = null
    )
    {
        ShowRewardedAdForCoins(
            GameEconomyConfig.Current.rewardedAdCoins,
            onRewarded
        );
    }

    public void ShowRewardedAdForCoins(
        int rewardAmount,
        Action<int> onRewarded = null
    )
    {
        rewardAmount = Mathf.Max(0, rewardAmount);
        if (rewardAmount <= 0 || !AdsSupported || rewardedAdShowing
            || PresentationGate.IsActive(PresentationGate.Kind.IapPurchase))
        {
            return;
        }

        if (rewardedAd == null || !rewardedAd.CanShowAd())
        {
            Debug.Log(
                "Rewarded reklam hazır değil, yeniden yükleniyor."
            );

            LoadRewardedAd();
            return;
        }

        pendingRewardAmount = rewardAmount;

        pendingRewardCallback = onRewarded;
        rewardedAdShowing = true;

        bool rewardGranted = false;

        rewardedAd.OnAdFullScreenContentClosed +=
            OnRewardedClosed;

        rewardedAd.OnAdFullScreenContentFailed +=
            OnRewardedFailed;

        PresentationGate.AcquireAdvertisement();
        rewardedAd.Show(reward =>
        {
            if (rewardGranted)
            {
                return;
            }

            rewardGranted = true;

            if (CoinManager.instance != null)
            {
                CoinManager.instance.AddCoins(
                    pendingRewardAmount
                );
            }

            pendingRewardCallback?.Invoke(
                pendingRewardAmount
            );

            pendingRewardCallback = null;
        });
    }

    // ─── Rewarded (placement-agnostic) ────────────────────────────────────────

    // Used by Continue and Fuel+3, which carry no coin amount of their own —
    // AdService routes both through this via AdManagerRewardedProvider so every
    // placement shares the one real RewardedAd instance and its rewardedAdShowing
    // guard, keeping two placements from ever showing at once.
    public void ShowRewardedAd(
        Action onRewardGranted,
        Action onClosedWithoutReward
    )
    {
        if (!AdsSupported || rewardedAdShowing
            || PresentationGate.IsActive(PresentationGate.Kind.IapPurchase))
        {
            onClosedWithoutReward?.Invoke();
            return;
        }

        if (rewardedAd == null || !rewardedAd.CanShowAd())
        {
            Debug.Log(
                "Rewarded reklam hazır değil, yeniden yükleniyor."
            );

            LoadRewardedAd();
            onClosedWithoutReward?.Invoke();
            return;
        }

        pendingGenericGrantedCallback = onRewardGranted;
        pendingGenericClosedCallback = onClosedWithoutReward;
        rewardedAdShowing = true;

        bool rewardGranted = false;

        rewardedAd.OnAdFullScreenContentClosed +=
            OnRewardedClosed;

        rewardedAd.OnAdFullScreenContentFailed +=
            OnRewardedFailed;

        PresentationGate.AcquireAdvertisement();
        rewardedAd.Show(reward =>
        {
            if (rewardGranted)
            {
                return;
            }

            rewardGranted = true;

            Action callback = pendingGenericGrantedCallback;
            pendingGenericGrantedCallback = null;
            pendingGenericClosedCallback = null;
            callback?.Invoke();
        });
    }

    private void OnRewardedClosed()
    {
        PresentationGate.ReleaseAdvertisement();
        UnsubscribeRewardedEvents();

        rewardedAdShowing = false;
        pendingRewardCallback = null;

        Action closedCallback = pendingGenericClosedCallback;
        pendingGenericGrantedCallback = null;
        pendingGenericClosedCallback = null;
        closedCallback?.Invoke();

        LoadRewardedAd();
    }

    private void OnRewardedFailed(AdError error)
    {
        PresentationGate.ReleaseAdvertisement();
        Debug.LogError(
            "Rewarded reklam gösterilemedi: " +
            error
        );

        UnsubscribeRewardedEvents();

        rewardedAdShowing = false;
        pendingRewardCallback = null;

        Action closedCallback = pendingGenericClosedCallback;
        pendingGenericGrantedCallback = null;
        pendingGenericClosedCallback = null;
        closedCallback?.Invoke();

        LoadRewardedAd();
    }

    private void UnsubscribeRewardedEvents()
    {
        if (rewardedAd == null)
        {
            return;
        }

        rewardedAd.OnAdFullScreenContentClosed -=
            OnRewardedClosed;

        rewardedAd.OnAdFullScreenContentFailed -=
            OnRewardedFailed;
    }

    private void DestroyRewardedAd()
    {
        if (rewardedAd == null)
        {
            return;
        }

        UnsubscribeRewardedEvents();
        rewardedAd.Destroy();
        rewardedAd = null;
    }
}
