using UnityEngine;

[CreateAssetMenu(fileName = "GameEconomyConfig", menuName = "Tap or Crash/Game Economy Config")]
public sealed class GameEconomyConfig : ScriptableObject
{
    [System.Serializable]
    public struct MilestoneReward
    {
        [Min(1)] public int score;
        [Min(0)] public int coins;
        [Min(0)] public int diamonds;
    }

    private const string ResourceName = "GameEconomyConfig";

    [Header("Landing Rewards")]
    [Min(0)] public int landingBaseReward = 1;
    [Min(1)] public int comboStep = 3;
    [Min(0)] public int maxComboBonus = 0;
    [Min(1)] public int levelStep = 10;
    [Min(0)] public int maxLevelBonus = 0;

    [Header("Milestones")]
    public MilestoneReward[] milestones =
    {
        new MilestoneReward { score = 10, coins = 0, diamonds = 0 },
        new MilestoneReward { score = 25, coins = 0, diamonds = 0 },
        new MilestoneReward { score = 50, coins = 0, diamonds = 1 },
        new MilestoneReward { score = 100, coins = 0, diamonds = 2 },
        new MilestoneReward { score = 150, coins = 0, diamonds = 3 },
        new MilestoneReward { score = 200, coins = 0, diamonds = 5 },
    };

    [Header("Shop")]
    public int[] skinPrices = { 0, 25, 50, 100 };

    [Header("Ads")]
    [Min(0)] public int rewardedAdCoins = 10;

    private static GameEconomyConfig current;

    public static GameEconomyConfig Current
    {
        get
        {
            if (current != null) return current;

            current = Resources.Load<GameEconomyConfig>(ResourceName);
            if (current == null)
            {
                current = CreateInstance<GameEconomyConfig>();
                current.hideFlags = HideFlags.HideAndDontSave;
                Debug.LogWarning("GameEconomyConfig was not found in Resources. Runtime defaults are being used.");
            }

            return current;
        }
    }

    public int GetLandingReward(int score, int combo)
    {
        return Mathf.Max(0, landingBaseReward);
    }

    public int GetSkinPrice(int index)
    {
        if (skinPrices == null || index < 0 || index >= skinPrices.Length) return 0;
        return Mathf.Max(0, skinPrices[index]);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        landingBaseReward = Mathf.Max(0, landingBaseReward);
        comboStep = Mathf.Max(1, comboStep);
        maxComboBonus = Mathf.Max(0, maxComboBonus);
        levelStep = Mathf.Max(1, levelStep);
        maxLevelBonus = Mathf.Max(0, maxLevelBonus);
        rewardedAdCoins = Mathf.Max(0, rewardedAdCoins);

        if (milestones == null || milestones.Length == 0)
        {
            milestones = new[]
            {
                new MilestoneReward { score = 10, coins = 0, diamonds = 0 },
                new MilestoneReward { score = 25, coins = 0, diamonds = 0 },
                new MilestoneReward { score = 50, coins = 0, diamonds = 1 },
                new MilestoneReward { score = 100, coins = 0, diamonds = 2 },
                new MilestoneReward { score = 150, coins = 0, diamonds = 3 },
                new MilestoneReward { score = 200, coins = 0, diamonds = 5 },
            };
        }

        for (int i = 0; i < milestones.Length; i++)
        {
            MilestoneReward value = milestones[i];
            value.score = Mathf.Max(1, value.score);
            value.coins = 0;
            value.diamonds = Mathf.Max(0, value.diamonds);
            milestones[i] = value;
        }

        if (skinPrices == null || skinPrices.Length != 4)
            skinPrices = new[] { 0, 25, 50, 100 };

        for (int i = 0; i < skinPrices.Length; i++)
            skinPrices[i] = Mathf.Max(0, skinPrices[i]);
    }
#endif
}
