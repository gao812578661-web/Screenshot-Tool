using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace RefScrn.Services
{
    public class AppSettings
    {
        public bool AutoStart { get; set; } = false;
        public string Hotkey { get; set; } = "Alt+A";
        public string DefaultSavePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    public class SettingsService
    {
        private const string ConfigFileName = "settings.json";
        private readonly string _configPath;
        public AppSettings CurrentSettings { get; private set; } = new AppSettings();

        public SettingsService()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    CurrentSettings = settings ?? new AppSettings();
                }
                else
                {
                    CurrentSettings = new AppSettings();
                }
            }
            catch
            {
                CurrentSettings = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
                SetAutoStart(CurrentSettings.AutoStart);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string keyName = "RefScrn";
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var mainModule = process.MainModule;
                if (mainModule == null) return;
                
                string assemblyLocation = mainModule.FileName;
                if (string.IsNullOrEmpty(assemblyLocation)) return;
                
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(keyName, assemblyLocation);
                        }
                        else
                        {
                            key.DeleteValue(keyName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting auto-start: {ex.Message}");
            }
        }
    }
}