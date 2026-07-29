using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// [RuntimeInitializeOnLoadMethod] fires once per launch, not once per scene. Every
// runtime-built layer registered through it therefore disappeared the first time the
// scene reloaded — restarting a run, or returning to the main menu from Game Over —
// leaving the unstyled scene objects behind (the "old" main menu).
//
// Installers registered here run immediately and again after every scene load. They are
// all idempotent (they bail out when their instance already exists), so re-running is free.
public static class SceneInstaller
{
    static readonly List<Action> installers = new List<Action>();
    static bool hooked;

    public static void RunOnEveryScene(Action installer)
    {
        if (installer == null) return;

        // Delegate equality is by target + method, so a method group registered twice
        // (domain reload disabled) is recognised as the same installer.
        if (!installers.Contains(installer)) installers.Add(installer);

        if (!hooked)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            hooked = true;
        }

        installer();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        for (int i = 0; i < installers.Count; i++) installers[i]?.Invoke();
    }
}
