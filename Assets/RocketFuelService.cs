using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Where an accepted Fuel grant came from. Fuel is a currency, so every credit
/// names its own origin rather than arriving anonymously.
public enum FuelGrantSource
{
    NaturalRefill,
    RewardedAd,
    DebugValidation,
}

[DisallowMultipleComponent]
public sealed class RocketFuelService : MonoBehaviour
{
    public const int Capacity = 20;
    public static readonly TimeSpan RefillInterval = TimeSpan.FromMinutes(15d);

    internal const string VersionKey = "RocketFuel.Version";
    internal const string CurrentKey = "RocketFuel.Current";
    internal const string RefillAnchorUtcKey = "RocketFuel.RefillAnchorUtcTicks";
    private const int PersistenceVersion = 1;

    private static RocketFuelService instance;
    private int currentFuel;
    private DateTime refillAnchorUtc;
    private float nextClockRefresh;
    private bool initialized;

    public static RocketFuelService Instance => Ensure();
    public int CurrentFuel => currentFuel;
    public int MaxFuel => Capacity;
    public float NormalizedFuel => Capacity > 0 ? currentFuel / (float)Capacity : 0f;
    public bool CanStartNewRun => currentFuel > 0;
    public TimeSpan TimeUntilNextFuel => CalculateTimeUntilNextFuel(DateTime.UtcNow);

    public event Action FuelChanged;
    public event Action NewRunRejected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() => Ensure();

    public static RocketFuelService Ensure()
    {
        if (instance != null) return instance;
        instance = FindAnyObjectByType<RocketFuelService>();
        if (instance != null) return instance;

        GameObject host = new GameObject("RocketFuelService");
        instance = host.AddComponent<RocketFuelService>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        LoadState(DateTime.UtcNow);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextClockRefresh) return;
        nextClockRefresh = Time.unscaledTime + 1f;
        RefreshFromClock();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshFromClock();

    private void OnApplicationPause(bool paused)
    {
        if (paused) Persist();
        else RefreshFromClock();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) RefreshFromClock();
    }

    private void OnApplicationQuit() => Persist();

    public bool TryConsumeForNewRun()
    {
        RefreshFromClock();
        if (currentFuel <= 0)
        {
            NewRunRejected?.Invoke();
            return false;
        }

        DateTime now = DateTime.UtcNow;
        bool wasFull = currentFuel >= Capacity;
        currentFuel = Mathf.Clamp(currentFuel - 1, 0, Capacity);
        if (wasFull) refillAnchorUtc = now;

        Persist();
        FuelChanged?.Invoke();
        return true;
    }

    /// The only way Fuel is ever added from outside this service. UI never writes
    /// Fuel or PlayerPrefs itself; it asks here and reads back what was actually
    /// credited, which is what keeps a clamped grant honest on the screen.
    ///
    /// Returns the amount granted, which is zero when the tank was already full.
    public int GrantFuel(int amount, FuelGrantSource source)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("Rejected a non-positive Fuel grant from " + source + ".");
            return 0;
        }

        // Elapsed refills are settled first, so a grant can never be credited on
        // top of a stale balance and then clamped against the wrong ceiling.
        RefreshFromClock();

        int previousFuel = currentFuel;
        currentFuel = Mathf.Clamp(currentFuel + amount, 0, Capacity);
        int granted = currentFuel - previousFuel;
        if (granted <= 0) return 0;

        // Filling the tank retires the pending refill: the anchor restarts only
        // when the tank next drops below capacity. Below capacity the anchor is
        // left alone, so a grant never costs the player refill progress.
        if (currentFuel >= Capacity) refillAnchorUtc = DateTime.UtcNow;

        Persist();
        FuelChanged?.Invoke();
        return granted;
    }

    public void NotifyNewRunRejected()
    {
        RefreshFromClock();
        if (currentFuel <= 0) NewRunRejected?.Invoke();
    }

    public void RefreshFromClock()
    {
        if (!initialized) LoadState(DateTime.UtcNow);

        DateTime now = DateTime.UtcNow;
        int previousFuel = currentFuel;
        DateTime previousAnchor = refillAnchorUtc;
        ApplyElapsedTime(now);

        if (currentFuel != previousFuel || refillAnchorUtc != previousAnchor)
        {
            Persist();
            if (currentFuel != previousFuel) FuelChanged?.Invoke();
        }
    }

    internal void ApplyElapsedTime(DateTime nowUtc)
    {
        currentFuel = Mathf.Clamp(currentFuel, 0, Capacity);
        if (currentFuel >= Capacity)
        {
            currentFuel = Capacity;
            return;
        }

        TimeSpan elapsed = nowUtc - refillAnchorUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        int intervals = (int)(elapsed.Ticks / RefillInterval.Ticks);
        if (intervals <= 0) return;

        currentFuel = Mathf.Min(Capacity, currentFuel + intervals);
        refillAnchorUtc = currentFuel >= Capacity
            ? nowUtc
            : refillAnchorUtc.AddTicks(RefillInterval.Ticks * intervals);
    }

    internal TimeSpan CalculateTimeUntilNextFuel(DateTime nowUtc)
    {
        if (currentFuel >= Capacity) return TimeSpan.Zero;

        TimeSpan elapsed = nowUtc - refillAnchorUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        long remainder = elapsed.Ticks % RefillInterval.Ticks;
        long remaining = RefillInterval.Ticks - remainder;
        return TimeSpan.FromTicks(Math.Max(0L, remaining));
    }

    private void LoadState(DateTime nowUtc)
    {
        initialized = true;
        if (PlayerPrefs.GetInt(VersionKey, 0) != PersistenceVersion)
        {
            currentFuel = Capacity;
            refillAnchorUtc = nowUtc;
            Persist();
            return;
        }

        currentFuel = Mathf.Clamp(PlayerPrefs.GetInt(CurrentKey, Capacity), 0, Capacity);
        refillAnchorUtc = ReadUtcTimestamp(nowUtc);
        ApplyElapsedTime(nowUtc);
        Persist();
    }

    private DateTime ReadUtcTimestamp(DateTime fallbackUtc)
    {
        string raw = PlayerPrefs.GetString(RefillAnchorUtcKey, string.Empty);
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
            && ticks >= DateTime.MinValue.Ticks
            && ticks <= DateTime.MaxValue.Ticks)
        {
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        return fallbackUtc;
    }

    private void Persist()
    {
        PlayerPrefs.SetInt(VersionKey, PersistenceVersion);
        PlayerPrefs.SetInt(CurrentKey, Mathf.Clamp(currentFuel, 0, Capacity));
        PlayerPrefs.SetString(RefillAnchorUtcKey,
            refillAnchorUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
    }
}
