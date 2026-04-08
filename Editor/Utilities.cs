using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildDebugger
{
    class Utilities
    {
        public static string ResolveUIPath(string fileName)
        {
            return $"Packages/com.unity.build-debugger/Editor/UI/{fileName}";
        }


        public static void Log(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
        }

        public static void LogWarning(string message)
        {
            Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "{0}", message);
        }
    }
}