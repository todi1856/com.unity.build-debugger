using System.IO;
using System.Text;
using UnityEditor;

namespace Unity.BuildDebugger
{
    class BeeUtilities
    {
        private static string m_BeeWhyStandalonePath;

        private string GetBeeWhyStandalonePath()
        {
            if (m_BeeWhyStandalonePath != null)
                return m_BeeWhyStandalonePath;
            var path = Path.Combine(EditorApplication.applicationContentsPath, "Tools/BuildPipeline/BeeWhy/Bee.Why.Standalone.dll");
            if (File.Exists(path))
            {
                m_BeeWhyStandalonePath = path;
                return m_BeeWhyStandalonePath;
            }
            
            throw new FileNotFoundException($"Could not find Bee.Why.Standalone.dll at expected path: {path}");
        }

        public static string ExecuteBeeWhyStandalone(string nodeName)
        {
            var beeWhyPath = new BeeUtilities().GetBeeWhyStandalonePath();

            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{beeWhyPath}\" \"Library/Bee\" \"{nodeName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.EnvironmentVariables["DOWNSTREAM_STDOUT_CONSUMER_SUPPORTS_COLOR"] = "0";

            Utilities.Log($"{process.StartInfo.FileName} {process.StartInfo.Arguments}");

            var output = new StringBuilder();

            process.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                    output.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) => {
                if (args.Data != null)
                    output.AppendLine(args.Data);
            };


            process.Start();
            process.BeginOutputReadLine(); 
            process.BeginErrorReadLine();

            if (process.WaitForExit(5000))
            {
                // Process finished normally
                return output.ToString();
            }
            else
            {
                // Process is stuck!
                process.Kill();
                output.Append("Timed out.");
            }

            return output.ToString();
        }
    }
}