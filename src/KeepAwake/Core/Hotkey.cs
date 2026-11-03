using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeepAwake.Core
{
    /// <summary>A global hotkey: modifier flags (RegisterHotKey values) and a virtual-key code. <see cref="None"/> means disabled.</summary>
    public readonly struct Hotkey
    {
        public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

        public static readonly Hotkey None = new Hotkey(0, 0);

        public Hotkey(uint modifiers, uint virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        public uint Modifiers { get; }
        public uint VirtualKey { get; }
        public bool IsNone => VirtualKey == 0;

        /// <summary>
        /// Parses "Ctrl+Alt+K", "ctrl + shift + f5", "Win+1". Modifiers are Ctrl, Alt, Shift, Win; the key is a letter,
        /// a digit or F1-F24. An empty string is valid and means disabled; a key without a modifier, or a modifier
        /// without a key, is not. Shift alone is not a modifier here (it would swallow typing), and Alt+F4 is refused.
        /// </summary>
        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = None;
            if (string.IsNullOrWhiteSpace(text)) return true;
            uint mods = 0, key = 0;
            foreach (var raw in text.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) return false;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModControl; continue;
                    case "alt": mods |= ModAlt; continue;
                    case "shift": mods |= ModShift; continue;
                    case "win": case "windows": mods |= ModWin; continue;
                }
                if (key != 0 || !TryKey(part, out key)) return false;
            }
            if (key == 0 || (mods & ~ModShift) == 0) return false;
            if (mods == ModAlt && key == 0x73) return false;
            hotkey = new Hotkey(mods, key);
            return true;
        }

        private static bool TryKey(string part, out uint vk)
        {
            vk = 0;
            if (part.Length == 1)
            {
                char c = char.ToUpperInvariant(part[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = c; return true; }
                return false;
            }
            if ((part[0] == 'F' || part[0] == 'f')
                && int.TryParse(part.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && n >= 1 && n <= 24)
            {
                vk = (uint)(0x70 + n - 1);
                return true;
            }
            return false;
        }

        /// <summary>Canonical text such as "Ctrl+Alt+K"; empty when disabled.</summary>
        public override string ToString()
        {
            if (IsNone) return "";
            var parts = new List<string>(5);
            if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((Modifiers & ModShift) != 0) parts.Add("Shift");
            if ((Modifiers & ModWin) != 0) parts.Add("Win");
            parts.Add(VirtualKey >= 0x70 && VirtualKey <= 0x87 ? "F" + (VirtualKey - 0x70 + 1) : ((char)VirtualKey).ToString());
            return string.Join("+", parts);
        }
    }

    /// <summary>Pure rules for the power-related options.</summary>
    public static class PowerRules
    {
        public static bool ShouldStopOnBattery(bool active, bool settingOn, bool onBattery) => active && settingOn && onBattery;
    }
}
