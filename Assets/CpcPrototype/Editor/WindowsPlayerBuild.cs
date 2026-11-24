using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CpcPrototype.Editor
{
    /// <summary>Builds the enabled project scenes as a Windows 64-bit player in batch mode.</summary>
    public static class WindowsPlayerBuild
    {
        private const string OutputPathEnvironmentVariable = "UNITY_BUILD_OUTPUT";

        public static void Build()
        {
            string outputPath = Environment.GetEnvironmentVariable(OutputPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException(
                    $"Set the {OutputPathEnvironmentVariable} environment variable to the .exe output path.");
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured in Build Settings.");
            }

            string missingScene = scenes.FirstOrDefault(scene => !File.Exists(scene));
            if (missingScene != null)
            {
                throw new FileNotFoundException("An enabled build scene does not exist.", missingScene);
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException($"Invalid build output path: {fullOutputPath}");
            }

            Directory.CreateDirectory(outputDirectory);

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
            {
                throw new InvalidOperationException("Unity could not switch to the Windows 64-bit build target.");
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"Windows player build failed: {report.summary.result}; " +
                    $"errors={report.summary.totalErrors}; warnings={report.summary.totalWarnings}.");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log(
                $"Windows player build succeeded: {fullOutputPath}; " +
                $"size={report.summary.totalSize} bytes; scenes={scenes.Length}.");
        }
    }
}
