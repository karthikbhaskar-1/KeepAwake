using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KeepAwake.Core
{
    public enum ThemeMode { System, Light, Dark }

    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "lastDurationMinutes")]
        public int LastDurationMinutes { get; set; } = Durations.DefaultMinutes;

        [DataMember(Name = "simulateActivity")]
        public bool SimulateActivity { get; set; }

        [DataMember(Name = "theme")]
        public string Theme { get; set; } = "System";

        public ThemeMode ThemeMode
        {
            get => Enum.TryParse(Theme, true, out ThemeMode m) ? m : ThemeMode.System;
            set => Theme = value.ToString();
        }

        public static Settings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new Settings();
                using (var fs = File.OpenRead(path))
                {
                    var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs) ?? new Settings();
                    bool known = s.LastDurationMinutes == Durations.ForeverMinutes
                                 || Durations.FromMinutes(s.LastDurationMinutes).Minutes == s.LastDurationMinutes;
                    if (!known) s.LastDurationMinutes = Durations.DefaultMinutes;
                    if (s.Theme == null) s.Theme = "System";
                    return s;
                }
            }
            catch
            {
                return new Settings();
            }
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(ms, this);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }
    }

    /// <summary>Settings live beside the exe; if that folder is not writable, under %LOCALAPPDATA%\KeepAwake.</summary>
    public static class SettingsStore
    {
        public const string FileName = "keepawake.json";

        public static string ResolvePath(string exeDirectory)
        {
            var beside = Path.Combine(exeDirectory, FileName);
            if (File.Exists(beside) || IsWritable(exeDirectory)) return beside;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeepAwake", FileName);
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".keepawake-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "", Encoding.ASCII);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
