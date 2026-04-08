using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

namespace Unity.BuildDebugger
{
    public class BuildPostprocessor
    {
        [PostProcessBuildAttribute(1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            var settings = UserSettings.GetOrLoad();
            if (!settings.AutoLoadPlayerDagJson)
                return;

            var window = MainWindow.Open();
            window.LoadDagJsonAndTundra(GetNewestFile("Library/Bee", "Player*.json")
                , GetNewestFile("Library/Bee", "tundra.log.json"));
        }

        private static string GetNewestFile(string beeFolderPath, string filter)
        {
            if (!Directory.Exists(beeFolderPath))
                return null;

            var newestFile = Directory
                .EnumerateFiles(beeFolderPath, filter, SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            return newestFile?.FullName;
        }
    }
}