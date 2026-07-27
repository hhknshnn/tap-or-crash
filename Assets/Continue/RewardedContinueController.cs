using UnityEngine;

[DisallowMultipleComponent]
public sealed class RewardedContinueController : MonoBehaviour, IRewardReceiver
{
    [Header("Continue Rules")]
    [SerializeField, Range(0f, 1f)] private float soCloseThreshold = 0.90f;

    private GameManager gameManager;
    private AdService adService;
    private ContinuePanelView panel;
    private RocketController.ContinueState rocketState;
    private CameraFollow.ContinueState cameraState;
    private int checkpointScore;
    private int checkpointCombo;
    private bool hasCheckpoint;
    private bool normalContinueUsed;
    private bool soCloseContinueUsed;
    private bool requestInProgress;
    private ContinuePanelView.OfferType activeOffer;

    public bool ContinueUsed => normalContinueUsed;
    public bool HasCheckpoint => hasCheckpoint;

    public void Initialize(GameManager manager, AdService service)
    {
        gameManager = manager;
        adService = service;
        panel = GetComponent<ContinuePanelView>();
        if (panel == null)
            panel = gameObject.AddComponent<ContinuePanelView>();
        panel.Initialize();
    }

    public void ResetForLevel()
    {
        hasCheckpoint = false;
        normalContinueUsed = false;
        soCloseContinueUsed = false;
        requestInProgress = false;
        activeOffer = ContinuePanelView.OfferType.Normal;
    }

    public void CaptureCheckpoint()
    {
        if (gameManager == null || gameManager.playerRocket == null)
            return;

        CameraFollow cameraFollow = GetCameraFollow();
        if (cameraFollow == null ||
            !gameManager.playerRocket.TryCaptureContinueState(out RocketController.ContinueState nextRocketState))
            return;

        rocketState = nextRocketState;
        cameraState = cameraFollow.CaptureContinueState();
        checkpointScore = gameManager.GetScore();
        checkpointCombo = gameManager.GetCombo();
        hasCheckpoint = true;
    }

    public bool TryShowOffer()
    {
        if (!hasCheckpoint || panel == null || adService == null)
            return false;

        if (!normalContinueUsed)
            activeOffer = ContinuePanelView.OfferType.Normal;
        else if (!soCloseContinueUsed && IsSoClose())
            activeOffer = ContinuePanelView.OfferType.SoClose;
        else
            return false;

        return panel.Show(activeOffer, adService.IsRewardAvailable, RequestReward, Decline);
    }

    public void OnRewardGranted()
    {
        if (!requestInProgress || IsActiveOfferUsed())
            return;

        requestInProgress = false;
        if (activeOffer == ContinuePanelView.OfferType.SoClose)
            soCloseContinueUsed = true;
        else
            normalContinueUsed = true;
        panel.Hide(ResumeFromCheckpoint);
    }

    public void OnRewardUnavailable()
    {
        requestInProgress = false;
        panel.SetUnavailable();
    }

    private void RequestReward()
    {
        if (requestInProgress || IsActiveOfferUsed() || adService == null)
            return;

        requestInProgress = true;
        panel.SetWaiting();
        adService.RequestReward(this);
    }

    private void Decline()
    {
        if (requestInProgress)
            return;

        panel.Hide(() =>
        {
            if (gameManager != null)
                gameManager.ShowNormalGameOver();
        });
    }

    private void ResumeFromCheckpoint()
    {
        if (gameManager == null)
            return;

        gameManager.ResumeFromContinue(rocketState, cameraState, checkpointScore, checkpointCombo);
    }

    private bool IsSoClose()
    {
        if (gameManager == null ||
            !gameManager.TryGetCurrentLevelProgress(out int currentPlanetIndex, out int totalPlanets))
            return false;

        float progress = currentPlanetIndex / (float)totalPlanets;
        return progress >= soCloseThreshold;
    }

    private bool IsActiveOfferUsed()
    {
        return activeOffer == ContinuePanelView.OfferType.SoClose
            ? soCloseContinueUsed
            : normalContinueUsed;
    }

    private static CameraFollow GetCameraFollow()
    {
        return Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
    }
}
