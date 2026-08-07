using System.Collections.Generic;
using UnityEngine;

// Small, bounded, versioned record of delivered IAP transaction ids so a
// pending order replayed after a crash, relaunch or store-side resend never
// grants its entitlement twice. Deliberately not a general-purpose ledger: it
// only ever needs to answer "have I already delivered this transaction id."
public sealed class PurchaseJournal
{
    private const string VersionKey = "IapJournal.Version";
    private const string EntriesKey = "IapJournal.Entries";
    private const int CurrentVersion = 1;
    private const int MaxEntries = 64;

    private readonly List<string> entries;

    public PurchaseJournal()
    {
        entries = Load();
    }

    public bool IsProcessed(string transactionId)
    {
        return !string.IsNullOrEmpty(transactionId) && entries.Contains(transactionId);
    }

    public void MarkProcessed(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId) || entries.Contains(transactionId)) return;

        entries.Add(transactionId);
        while (entries.Count > MaxEntries) entries.RemoveAt(0);
        Save();
    }

    private static List<string> Load()
    {
        if (PlayerPrefs.GetInt(VersionKey, 0) != CurrentVersion)
        {
            PlayerPrefs.SetInt(VersionKey, CurrentVersion);
            PlayerPrefs.SetString(EntriesKey, string.Empty);
            PlayerPrefs.Save();
            return new List<string>();
        }

        string raw = PlayerPrefs.GetString(EntriesKey, string.Empty);
        if (string.IsNullOrEmpty(raw)) return new List<string>();
        return new List<string>(raw.Split('\n'));
    }

    private void Save()
    {
        PlayerPrefs.SetString(EntriesKey, string.Join("\n", entries));
        PlayerPrefs.Save();
    }
}
