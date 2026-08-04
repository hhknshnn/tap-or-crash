using System;
using UnityEngine;

// The rewarded-ad context that pays in Fuel.
//
// It is deliberately its own controller rather than a flag on an existing one:
// Continue and the run-coin double each own their reward and their own "already
// claimed" rule, and a shared receiver would let one context's callback settle
// another context's request.
//
// Duplicate protection lives here, at the request level. A network that fires its
// reward callback twice — or a fake reward that races a real one — settles the
// request the first time and is ignored afterwards, so one tap can never buy six
// Fuel.
[DisallowMultipleComponent]
public sealed class FuelRewardController : MonoBehaviour, IRewardReceiver
{
    public const int RewardAmount = 3;

    private AdService adService;
    private Action<int> granted;
    private Action unavailable;
    private bool requestInProgress;

    public bool IsRequestInProgress => requestInProgress;

    public bool IsRewardAvailable
    {
        get
        {
            AdService service = ResolveAdService();
            return service != null && service.IsRewardAvailable;
        }
    }

    /// Finds the controller beside the project's AdService, creating it once.
    /// Returns null only when the scene has no AdService at all.
    public static FuelRewardController Ensure()
    {
        AdService service = FindAnyObjectByType<AdService>();
        if (service == null) return null;

        FuelRewardController controller = service.GetComponent<FuelRewardController>();
        if (controller == null) controller = service.gameObject.AddComponent<FuelRewardController>();
        controller.adService = service;
        return controller;
    }

    /// Starts one rewarded request. Returns false when a request is already in
    /// flight or no ad service exists, in which case no callback will fire.
    public bool TryRequestFuel(Action<int> onGranted, Action onUnavailable)
    {
        if (requestInProgress) return false;

        AdService service = ResolveAdService();
        if (service == null) return false;

        requestInProgress = true;
        granted = onGranted;
        unavailable = onUnavailable;
        service.RequestReward(this);
        return true;
    }

    /// Lets a popup that is closing stop hearing about a request it no longer
    /// owns. The Fuel itself is still granted: the player watched the ad.
    public void DetachListeners()
    {
        granted = null;
        unavailable = null;
    }

    public void OnRewardGranted()
    {
        // Not in progress means this is a repeat of a callback already settled.
        if (!requestInProgress) return;
        requestInProgress = false;

        Action<int> callback = granted;
        DetachListeners();

        int amount = RocketFuelService.Instance.GrantFuel(RewardAmount, FuelGrantSource.RewardedAd);
        callback?.Invoke(amount);
    }

    public void OnRewardUnavailable()
    {
        if (!requestInProgress) return;
        requestInProgress = false;

        Action callback = unavailable;
        DetachListeners();
        callback?.Invoke();
    }

    private AdService ResolveAdService()
    {
        if (adService == null) adService = GetComponent<AdService>();
        if (adService == null) adService = FindAnyObjectByType<AdService>();
        return adService;
    }
}
