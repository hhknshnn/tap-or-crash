using UnityEngine;

[DisallowMultipleComponent]
public sealed class AdService : MonoBehaviour
{
    [Header("Editor Test Mode")]
    [Tooltip("Only ever honoured inside the Unity Editor. Device and production " +
        "builds always use the real rewarded provider below, regardless of this flag.")]
    [SerializeField] private bool useFakeReward = true;

    private readonly RewardedAdController rewardedAds = new RewardedAdController();

    // Editor-only escape hatch. Forcing this false outside the Editor is what
    // keeps a forgotten Inspector checkbox from shipping mock rewards to players.
    private bool EffectiveUseFakeReward => useFakeReward && Application.isEditor;

    public bool UseFakeReward
    {
        get => useFakeReward;
        set => useFakeReward = value;
    }

    public bool IsRewardAvailable => rewardedAds.IsAvailable(EffectiveUseFakeReward);

    private void Awake()
    {
        SetRewardedProvider(new AdManagerRewardedProvider());
    }

    public void RequestReward(IRewardReceiver receiver)
    {
        rewardedAds.RequestReward(EffectiveUseFakeReward, receiver);
    }

    public void SetRewardedProvider(IRewardedAdProvider provider)
    {
        rewardedAds.SetProvider(provider);
    }
}
