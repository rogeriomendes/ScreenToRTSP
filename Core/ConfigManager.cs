using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ScreenToRTSP.Core
{
    public class ConfigManager
    {
        private readonly string configPath;

        public ConfigManager()
        {
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".ini");
        }

        public string EncryptPassword(string clearText)
        {
            if (string.IsNullOrEmpty(clearText)) return "";
            byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
            for (int i = 0; i < clearBytes.Length; i++) clearBytes[i] ^= 0x42;
            return Convert.ToBase64String(clearBytes);
        }

        public string DecryptPassword(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            try {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                for (int i = 0; i < cipherBytes.Length; i++) cipherBytes[i] ^= 0x42;
                return Encoding.UTF8.GetString(cipherBytes);
            } catch { return ""; }
        }

        public Dictionary<string, string> Load()
        {
            var config = new Dictionary<string, string>();
            if (File.Exists(configPath))
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    int idx = line.IndexOf('=');
                    if (idx > 0) config[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }
            }
            return config;
        }

        public void Save(List<string> lines)
        {
            try { File.WriteAllLines(configPath, lines); } catch { }
        }
        
        public bool IsFirstRun()
        {
            return !File.Exists(configPath);
        }
    }
}
