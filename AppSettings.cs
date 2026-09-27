using System;
using System.IO;
using System.Text.Json;

namespace RaylibUltralightApp
{
    public class AppSettings
    {
        public int Width { get; set; } = 1024;
        public int Height { get; set; } = 768;
        public bool Fullscreen { get; set; } = false;
        public int FpsCap { get; set; } = 60;

        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] Failed to load settings.json: {ex.Message}");
            }

            var defaultSettings = new AppSettings();
            defaultSettings.Save();
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(FilePath, json);
                Console.WriteLine("[Settings] Saved settings to settings.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] Failed to save settings.json: {ex.Message}");
            }
        }
    }
}
