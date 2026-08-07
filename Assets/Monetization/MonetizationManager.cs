using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;

// Single authority for Unity IAP: connect, fetch, purchase, deliver, confirm,
// restore. Rewarded-ad placements keep their own dedicated controllers
// (RewardedContinueController, FuelRewardController, and AdManager's Game
// Over x2 / Shop coin rewards) — each already owns its placement's duplicate
// protection, so this does not re-wrap them. What this adds is the IAP side
// and the shared PresentationGate.Kind.IapPurchase gate those controllers,
// the Shop and Fuel popups, and the interstitial all consult so an ad, a
// purchase sheet and a popup can never overlap.
[DisallowMultipleComponent]
public sealed class MonetizationManager : MonoBehaviour
{
    public enum IapState
    {
        Uninitialized,
        Connecting,
        Ready,
        Unavailable,
    }

    private const string OwnershipKeyPrefix = "iap_owned_";

    public static MonetizationManager instance;

    public event Action ProductsUpdated;
    public event Action<string> PurchaseSucceeded;
    public event Action<string, string> PurchaseFailed;

    private StoreController storeController;
    private PurchaseJournal journal;
    private IapState state = IapState.Uninitialized;
    private bool purchaseInFlight;
    private bool purchasesFetchRequested;

    // Products the store has deferred (e.g. parental approval pending). Kept
    // separate from purchaseInFlight, which only covers the single in-flight
    // request: a deferred product stays blocked from repurchase across many
    // frames — possibly across app restarts, via OnPurchasesFetched — while
    // every other product must remain purchasable.
    private readonly HashSet<string> deferredProductIds = new HashSet<string>();

    public IapState State => state;
    public bool IsPurchaseInFlight => purchaseInFlight;
    public bool IsPurchaseDeferred(string productId) => deferredProductIds.Contains(productId);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    private static void Install()
    {
        if (FindAnyObjectByType<MonetizationManager>() != null) return;
        new GameObject("MonetizationManager").AddComponent<MonetizationManager>();
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        journal = new PurchaseJournal();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private async void Start()
    {
        state = IapState.Connecting;

        storeController = UnityIAPServices.StoreController();
        storeController.OnPurchasePending += OnPurchasePending;
        storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
        storeController.OnPurchaseFailed += OnPurchaseFailedBeforePending;
        storeController.OnPurchaseDeferred += OnPurchaseDeferred;
        storeController.OnProductsFetched += OnProductsFetched;
        storeController.OnProductsFetchFailed += OnProductsFetchFailed;
        storeController.OnPurchasesFetched += OnPurchasesFetched;
        storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
        storeController.OnStoreConnected += OnStoreConnected;
        storeController.OnStoreDisconnected += OnStoreDisconnected;

        try
        {
            await storeController.Connect();
        }
        catch (Exception exception)
        {
            Debug.LogError("MonetizationManager: store connect failed: " + exception);
            state = IapState.Unavailable;
            ProductsUpdated?.Invoke();
            return;
        }

        storeController.FetchProducts(MonetizationProducts.Catalog.ToList());
    }

    private void OnStoreConnected()
    {
        Debug.Log("MonetizationManager: store connected.");
    }

    private void OnStoreDisconnected(StoreConnectionFailureDescription description)
    {
        Debug.LogWarning("MonetizationManager: store disconnected: " + description);
        state = IapState.Unavailable;
        ProductsUpdated?.Invoke();
    }

    // ── Product metadata ────────────────────────────────────────────────────

    private void OnProductsFetched(System.Collections.Generic.List<Product> products)
    {
        state = IapState.Ready;
        ProductsUpdated?.Invoke();

        // Restoring purchase history only makes sense once products are known,
        // and only needs to happen once per app run — the store resends every
        // owned non-consumable on this single fetch, there is nothing further
        // to gain from repeating it later in the same session.
        if (!purchasesFetchRequested)
        {
            purchasesFetchRequested = true;
            Debug.Log("MonetizationManager: purchase history fetch started.");
            storeController.FetchPurchases();
        }
    }

    private void OnProductsFetchFailed(ProductFetchFailed failure)
    {
        Debug.LogError("MonetizationManager: product fetch failed: " + failure);
        state = IapState.Unavailable;
        ProductsUpdated?.Invoke();
    }

    // ── Restore (non-consumable Rockets only) ──────────────────────────────

    // Restores ownership for Cat, Dog and Retro UFO from confirmed purchase
    // history. Never touches fuel_full_refill — it is a consumable, and
    // purchase history is not a record of how many refills are still owed.
    // Runs silently: no success animation, no auto-equip, just ownership and
    // a Shop refresh, exactly like the app already does on every other launch
    // once these PlayerPrefs keys are set.
    private void OnPurchasesFetched(Orders orders)
    {
        int confirmedCount = orders.ConfirmedOrders.Count;
        int restoredCount = 0;

        for (int i = 0; i < confirmedCount; i++)
        {
            ConfirmedOrder order = orders.ConfirmedOrders[i];
            CartItem item = order.CartOrdered?.Items()?.FirstOrDefault();
            string productId = item?.Product?.definition?.id;

            if (string.IsNullOrEmpty(productId) || !MonetizationProducts.IsRocketProduct(productId))
                continue;

            DeliverEntitlement(productId);
            // A confirmed non-consumable always outranks a stale deferred flag —
            // the same product cannot be simultaneously owned and pending.
            deferredProductIds.Remove(productId);
            restoredCount++;
        }

        // The deferred set is fully replaced from this fetch's authoritative
        // DeferredOrders rather than merged, so a product the store no longer
        // reports as deferred (approved, denied, or expired externally) stops
        // showing PURCHASE PENDING instead of being stuck there forever.
        int deferredCount = orders.DeferredOrders.Count;
        HashSet<string> rebuiltDeferred = new HashSet<string>();
        for (int i = 0; i < deferredCount; i++)
        {
            DeferredOrder order = orders.DeferredOrders[i];
            CartItem item = order.CartOrdered?.Items()?.FirstOrDefault();
            string productId = item?.Product?.definition?.id;
            if (!string.IsNullOrEmpty(productId) && MonetizationProducts.IsKnownProduct(productId))
                rebuiltDeferred.Add(productId);
        }
        bool deferredChanged = !deferredProductIds.SetEquals(rebuiltDeferred);
        deferredProductIds.Clear();
        foreach (string productId in rebuiltDeferred) deferredProductIds.Add(productId);

        Debug.Log("MonetizationManager: purchase history fetch completed. Confirmed orders inspected: "
            + confirmedCount + ", Rockets restored: " + restoredCount
            + ", deferred products: " + deferredProductIds.Count);

        if (restoredCount > 0 || deferredChanged) ProductsUpdated?.Invoke();
    }

    // An empty or failed fetch must never revoke ownership already stored
    // locally — this client has no other source of truth, so it only ever
    // adds or reaffirms entitlement, never removes it. The IAP state stays
    // whatever OnProductsFetched already set: products are still usable even
    // though history could not be read.
    private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning("MonetizationManager: purchase history fetch failed: " + failure.FailureReason);
    }

    public Product GetProduct(string productId) =>
        storeController?.GetProducts().FirstOrDefault(p => p.definition.id == productId);

    public bool TryGetLocalizedPrice(string productId, out string localizedPrice)
    {
        Product product = GetProduct(productId);
        if (product != null && product.availableToPurchase && product.metadata != null)
        {
            localizedPrice = product.metadata.localizedPriceString;
            return true;
        }

        localizedPrice = null;
        return false;
    }

    // ── Purchasing ───────────────────────────────────────────────────────────

    public bool CanPurchase(string productId)
    {
        if (state != IapState.Ready || purchaseInFlight || PresentationGate.IsAdvertisementActive
            || IsPurchaseDeferred(productId))
            return false;

        Product product = GetProduct(productId);
        return product != null && product.availableToPurchase;
    }

    public bool Purchase(string productId)
    {
        if (!CanPurchase(productId)) return false;

        Product product = GetProduct(productId);
        if (product == null) return false;

        purchaseInFlight = true;
        PresentationGate.Acquire(PresentationGate.Kind.IapPurchase);
        storeController.PurchaseProduct(product);
        return true;
    }

    // The store has already granted this transaction by the time it reaches us
    // as a PendingOrder — entitlement is delivered and persisted here, before
    // ConfirmPurchase, so a crash between the two loses at most a confirm ack
    // (recovered by the store resending the same order) and never the reward.
    private void OnPurchasePending(PendingOrder order)
    {
        CartItem item = order.CartOrdered?.Items()?.FirstOrDefault();
        string productId = item?.Product?.definition?.id;
        string transactionId = order.Info?.TransactionID;

        // A deferred order that was later approved arrives here as a normal
        // PendingOrder — it is no longer waiting, so the pending UI must clear
        // before entitlement is delivered.
        if (!string.IsNullOrEmpty(productId)) deferredProductIds.Remove(productId);

        if (!string.IsNullOrEmpty(productId) && !string.IsNullOrEmpty(transactionId)
            && !journal.IsProcessed(transactionId))
        {
            DeliverEntitlement(productId);
            journal.MarkProcessed(transactionId);
        }

        storeController.ConfirmPurchase(order);
    }

    // Parental approval required, store-side hold, etc. The purchase sheet has
    // already been dismissed by the time this fires, so the active purchase
    // gate is released here exactly as it is on success or failure — nothing
    // is granted, nothing is journaled, and the product stays marked pending
    // until a later OnPurchasePending, OnPurchaseFailed or OnPurchasesFetched
    // resolves it.
    private void OnPurchaseDeferred(DeferredOrder order)
    {
        purchaseInFlight = false;
        PresentationGate.Release(PresentationGate.Kind.IapPurchase);

        CartItem item = order.CartOrdered?.Items()?.FirstOrDefault();
        string productId = item?.Product?.definition?.id;

        if (string.IsNullOrEmpty(productId) || !MonetizationProducts.IsKnownProduct(productId))
        {
            Debug.LogWarning("MonetizationManager: deferred order for an unrecognized product was ignored.");
            ProductsUpdated?.Invoke();
            return;
        }

        deferredProductIds.Add(productId);
        Debug.Log("MonetizationManager: purchase deferred: " + productId);
        ProductsUpdated?.Invoke();
    }

    private void DeliverEntitlement(string productId)
    {
        if (productId == MonetizationProducts.FuelFullRefill)
        {
            RocketFuelService.Instance.GrantFullRefill(FuelGrantSource.Purchase);
            return;
        }

        if (MonetizationProducts.IsRocketProduct(productId))
        {
            PlayerPrefs.SetInt(OwnershipKeyPrefix + productId, 1);
            PlayerPrefs.Save();
        }
    }

    public bool IsRocketOwned(string productId) =>
        PlayerPrefs.GetInt(OwnershipKeyPrefix + productId, 0) == 1;

    private void OnPurchaseConfirmed(Order order)
    {
        purchaseInFlight = false;
        PresentationGate.Release(PresentationGate.Kind.IapPurchase);

        string productId = order.CartOrdered?.Items()?.FirstOrDefault()?.Product?.definition?.id;
        if (!string.IsNullOrEmpty(productId)) deferredProductIds.Remove(productId);

        if (order is FailedOrder failedOrder)
            PurchaseFailed?.Invoke(productId, failedOrder.FailureReason.ToString());
        else
            PurchaseSucceeded?.Invoke(productId);
    }

    private void OnPurchaseFailedBeforePending(FailedOrder order)
    {
        purchaseInFlight = false;
        PresentationGate.Release(PresentationGate.Kind.IapPurchase);

        string productId = order.CartOrdered?.Items()?.FirstOrDefault()?.Product?.definition?.id;
        if (!string.IsNullOrEmpty(productId)) deferredProductIds.Remove(productId);
        PurchaseFailed?.Invoke(productId, order.FailureReason.ToString());
    }
}
