using System;

public sealed class RewardedAdController
{
    private IRewardedAdProvider provider;
    private bool requestInProgress;

    public bool IsAvailable(bool useFakeReward)
    {
        return !requestInProgress && (useFakeReward || (provider != null && provider.IsReady));
    }

    public void SetProvider(IRewardedAdProvider rewardedProvider)
    {
        provider = rewardedProvider;
    }

    public void RequestReward(bool useFakeReward, IRewardReceiver receiver)
    {
        if (receiver == null || requestInProgress)
            return;

        if (useFakeReward)
        {
            receiver.OnRewardGranted();
            return;
        }

        if (provider == null || !provider.IsReady)
        {
            receiver.OnRewardUnavailable();
            return;
        }

        requestInProgress = true;
        provider.Show(
            () => Complete(receiver, true),
            () => Complete(receiver, false));
    }

    private void Complete(IRewardReceiver receiver, bool rewardGranted)
    {
        if (!requestInProgress)
            return;

        requestInProgress = false;
        if (rewardGranted)
            receiver.OnRewardGranted();
        else
            receiver.OnRewardUnavailable();
    }
}
