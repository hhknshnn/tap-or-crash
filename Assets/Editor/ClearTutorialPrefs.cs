using UnityEditor;
using UnityEngine;

public static class ClearTutorialPrefs
{
    [MenuItem("Tools/Tap or Crash/Clear Tutorial Progress")]
    private static void Clear()
    {
        PlayerPrefs.DeleteKey("Tutorial.CompletedVersion");
        PlayerPrefs.DeleteKey("TutorialShown");
        PlayerPrefs.Save();

        Debug.Log("Tutorial PlayerPrefs cleared.");
    }
}