using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    private const string DefaultOutputPath = "build/ios";

    public static void BuildIos()
    {
        string outputPath =
            Environment.GetEnvironmentVariable("UNITY_IOS_EXPORT_PATH");

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = DefaultOutputPath;
        }

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException(
                "Build edilecek aktif sahne bulunamadı."
            );
        }

        PlayerSettings.SetApplicationIdentifier(
            BuildTargetGroup.iOS,
            "com.VexoriaLabs.TaporCrash"
        );

        PlayerSettings.bundleVersion = "1.4";
        PlayerSettings.iOS.buildNumber =
            Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "1";

        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;

        Directory.CreateDirectory(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new Exception(
                $"iOS export başarısız. Sonuç: {summary.result}, " +
                $"Hata: {summary.totalErrors}"
            );
        }

        Debug.Log(
            $"iOS export başarılı: {summary.outputPath}"
        );
    }
}