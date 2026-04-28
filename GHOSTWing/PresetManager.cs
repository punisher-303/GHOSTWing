using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GHOSTWing
{
    public class PresetManager
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GHOSTWing",
            "presets.json");

        public List<RecoilPreset> Presets { get; private set; } = new List<RecoilPreset>();

        public void Load()
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                Presets = JsonSerializer.Deserialize<List<RecoilPreset>>(json) ?? new List<RecoilPreset>();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(Presets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }

        public string GetFolderPath() => Path.GetDirectoryName(FilePath)!;

        public void AddOrUpdatePreset(RecoilPreset preset)
        {
            var existing = Presets.Find(p => p.Name == preset.Name);
            if (existing != null)
            {
                existing.Vertical = preset.Vertical;
                existing.Horizontal = preset.Horizontal;
                existing.Delay = preset.Delay;
                existing.ShortcutKey = preset.ShortcutKey;
            }
            else
            {
                Presets.Add(preset);
            }
            Save();
        }

        public void DeletePreset(string name)
        {
            Presets.RemoveAll(p => p.Name == name);
            Save();
        }
    }
}
