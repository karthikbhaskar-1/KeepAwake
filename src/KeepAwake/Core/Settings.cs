using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeepAwake.Core
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>The three persisted values. Read and written by hand so no serializer assembly is loaded at start-up.</summary>
    public sealed class Settings
    {
        public int LastDurationMinutes { get; set; } = Durations.DefaultMinutes;

        public bool SimulateActivity { get; set; }

        public string Theme { get; set; } = "System";

        public ThemeMode ThemeMode
        {
            get => ParseTheme(Theme);
            set => Theme = value.ToString();
        }

        /// <summary>Accepts the names System, Light and Dark (any case, surrounding spaces ignored); anything else is System.</summary>
        public static ThemeMode ParseTheme(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "light": return ThemeMode.Light;
                case "dark": return ThemeMode.Dark;
                default: return ThemeMode.System;
            }
        }

        public static Settings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new Settings();
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch
            {
                return new Settings();
            }
        }

        /// <summary>Starts from the defaults, so a key that is missing keeps its default value.</summary>
        public static Settings Parse(string json)
        {
            var s = new Settings();
            Dictionary<string, string> raw;
            try { raw = FlatJson.ReadObject(json); }
            catch { return s; }
            if (raw == null) return s;

            if (raw.TryGetValue("lastDurationMinutes", out var minutes)
                && int.TryParse(minutes, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var m)
                && m >= 0
                && (m == Durations.ForeverMinutes || Durations.FromMinutes(m).Minutes == m))
                s.LastDurationMinutes = m;
            if (raw.TryGetValue("simulateActivity", out var sim)) s.SimulateActivity = sim == "true";
            if (raw.TryGetValue("theme", out var theme)) s.ThemeMode = ParseTheme(theme);
            return s;
        }

        public string ToJson()
        {
            return "{\"lastDurationMinutes\":" + LastDurationMinutes.ToString(CultureInfo.InvariantCulture)
                + ",\"simulateActivity\":" + (SimulateActivity ? "true" : "false")
                + ",\"theme\":\"" + ThemeMode + "\"}";
        }

        /// <summary>Writes to a temp file and swaps it in, so a crash never leaves a truncated settings file.</summary>
        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, new UTF8Encoding(false).GetBytes(ToJson()));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }
    }

    /// <summary>Minimal reader for one flat JSON object. Values come back as raw text (strings unquoted); nested values are skipped.</summary>
    internal static class FlatJson
    {
        public static Dictionary<string, string> ReadObject(string text)
        {
            if (text == null) return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i++;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') return null;
            i++;
            while (true)
            {
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] == '}') return result;
                if (text[i] == ',') { i++; continue; }
                if (text[i] != '"') return result;
                var key = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':') return result;
                i++;
                SkipWs(text, ref i);
                if (i >= text.Length) return result;
                string value;
                if (text[i] == '"') value = ReadString(text, ref i);
                else if (text[i] == '{' || text[i] == '[') { SkipNested(text, ref i); value = null; }
                else
                {
                    int start = i;
                    while (i < text.Length && text[i] != ',' && text[i] != '}' && !char.IsWhiteSpace(text[i])) i++;
                    value = text.Substring(start, i - start);
                    if (value == "null") value = null;
                }
                if (value != null) result[key] = value;
            }
        }

        private static void SkipWs(string t, ref int i)
        {
            while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
        }

        private static string ReadString(string t, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < t.Length && t[i] != '"')
            {
                if (t[i] == '\\' && i + 1 < t.Length)
                {
                    i++;
                    char c = t[i];
                    if (c == 'u' && i + 4 < t.Length
                        && ushort.TryParse(t.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
                    {
                        sb.Append((char)cp);
                        i += 4;
                    }
                    else sb.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c);
                }
                else sb.Append(t[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        private static void SkipNested(string t, ref int i)
        {
            int depth = 0;
            for (; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '"') { ReadString(t, ref i); i--; }
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0) { i++; return; }
                }
            }
        }
    }

    /// <summary>Settings live beside the exe; if that file cannot be written, under %LOCALAPPDATA%\KeepAwake.</summary>
    public static class SettingsStore
    {
        public const string FileName = "keepawake.json";

        public static string FallbackPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeepAwake", FileName);

        /// <summary>Decides without writing anything: an existing fallback file wins, and a read-only file beside the exe sends us to it.</summary>
        public static string ResolvePath(string exeDirectory) => ResolvePath(exeDirectory, FallbackPath());

        public static string ResolvePath(string exeDirectory, string fallback)
        {
            var beside = Path.Combine(exeDirectory, FileName);
            if (File.Exists(fallback)) return fallback;
            if (File.Exists(beside)) return CanOpenForWrite(beside) ? beside : fallback;
            return beside;
        }

        private static bool CanOpenForWrite(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
