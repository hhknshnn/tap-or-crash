using UnityEngine;

[DisallowMultipleComponent]
public sealed class AdService : MonoBehaviour
{
    [Header("Editor Test Mode")]
    [SerializeField] private bool useFakeReward = true;

    private readonly RewardedAdController rewardedAds = new RewardedAdController();

    public bool UseFakeReward
    {
        get => useFakeReward;
        set => useFakeReward = value;
    }

    public bool IsRewardAvailable => rewardedAds.IsAvailable(useFakeReward);

    public void RequestReward(IRewardReceiver receiver)
    {
        rewardedAds.RequestReward(useFakeReward, receiver);
    }

    public void SetRewardedProvider(IRewardedAdProvider provider)
    {
        rewardedAds.SetProvider(provider);
    }
}
