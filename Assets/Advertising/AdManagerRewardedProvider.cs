using System;

// Bridges AdService's placement-based rewarded requests (Continue, Fuel+3) to
// AdManager's real AdMob RewardedAd. Without this, those placements only ever
// saw AdService.useFakeReward and never touched a real ad.
public sealed class AdManagerRewardedProvider : IRewardedAdProvider
{
    public bool IsReady => AdManager.instance != null && AdManager.instance.IsRewardedAdReady();

    public void Show(Action onRewardGranted, Action onClosedWithoutReward)
    {
        if (AdManager.instance == null)
        {
            onClosedWithoutReward?.Invoke();
            return;
        }

        AdManager.instance.ShowRewardedAd(onRewardGranted, onClosedWithoutReward);
    }
}
