using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unity.BuildDebugger
{
    class UserSettings
    {
        private static readonly string SettingsPath = Path.Combine("UserSettings", "BuildDebuggerSettings.asset");
        private static UserSettings s_Instance;

        [Serializable]
        internal class Setttings
        {
            [SerializeField]
            internal string LastDagJsonPath;

            [SerializeField]
            internal bool AutoLoadPlayerDagJson;
        }

        [SerializeField]
        private Setttings m_Settings;

        public string LastDagJsonPath
        {
            get => m_Settings.LastDagJsonPath;
            set => m_Settings.LastDagJsonPath = value;
        }

        public bool AutoLoadPlayerDagJson
        {
            get => m_Settings.AutoLoadPlayerDagJson;
            set => m_Settings.AutoLoadPlayerDagJson = value;
        }

        internal UserSettings()
        {
            Reset();
        }

        internal void Reset()
        {
            m_Settings = new Setttings()
            {
                LastDagJsonPath = "Assets",
                AutoLoadPlayerDagJson = true
            };
        }

        internal static UserSettings GetOrLoad()
        {
            if (s_Instance != null)
                return s_Instance;

            var path = SettingsPath;
            if (!File.Exists(path))
                return s_Instance = new UserSettings();

            var jsonString = File.ReadAllText(path);
            if (string.IsNullOrEmpty(jsonString))
                return s_Instance = new UserSettings();
            try
            {
                var settings = new UserSettings();
                JsonUtility.FromJsonOverwrite(jsonString, settings);
                return s_Instance = settings;
            }
            catch (Exception ex)
            {
                Utilities.Log("Load User Settings from json failed: " + ex.Message);
            }
            return s_Instance = new UserSettings();
        }

        internal static void Save()
        {
            var path = SettingsPath;
            if (s_Instance == null)
                return;

            var jsonString = JsonUtility.ToJson(s_Instance, true);
            if (string.IsNullOrEmpty(jsonString))
                return;

            File.WriteAllText(path, jsonString);
        }
    }
}
