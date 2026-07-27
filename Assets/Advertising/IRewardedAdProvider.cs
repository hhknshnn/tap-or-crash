using System;

public interface IRewardedAdProvider
{
    bool IsReady { get; }
    void Show(Action onRewardGranted, Action onClosedWithoutReward);
}
