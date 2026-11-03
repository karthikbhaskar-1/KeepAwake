using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeepAwake.Core;

namespace KeepAwake.UI
{
    public enum TrayOption { SimulateActivity, StopOnBattery, StopWhenLocked }

    /// <summary>Notification-area icon, tooltip, balloon toast and the right-click menu.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Icon _active, _inactive;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _show, _extend, _deactivate, _options, _theme, _quit;
        private ToolStripMenuItem _optSimulate, _optBattery, _optLocked;
        private bool _simulateOn, _batteryOn, _lockedOn;
        private Action _balloonAction;

        private bool _lastActive, _lastForever;
        private ThemeMode _lastTheme = ThemeMode.System;
        private string _lastText = "KeepAwake - Inactive";
        private bool _hasState, _disposed;

        public event EventHandler ShowRequested;
        public event EventHandler<DurationOption> ExtendRequested;
        public event EventHandler DeactivateRequested;
        public event EventHandler<ThemeMode> ThemeRequested;
        public event EventHandler QuitRequested;
        public event EventHandler ToggleRequested;
        public event EventHandler<TrayOption> OptionRequested;

        public TrayIcon()
        {
            _active = LoadIcon("tray_active.ico");
            _inactive = LoadIcon("tray_inactive.ico");

            _icon = new NotifyIcon
            {
                Icon = _inactive,
                Text = _lastText,
                Visible = true,
            };
            // The menu is built on first use (or shortly after start-up), not before the splash.
            _icon.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right) EnsureMenu(); };
            _icon.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) EnsureMenu(); };
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty);
                else if (e.Button == MouseButtons.Middle) ToggleRequested?.Invoke(this, EventArgs.Empty);
            };
            _icon.BalloonTipClicked += (s, e) =>
            {
                var a = _balloonAction;
                _balloonAction = null;
                a?.Invoke();
            };
            _icon.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty); };
        }

        /// <summary>Builds the right-click menu if it does not exist yet.</summary>
        public void EnsureMenu()
        {
            if (_menu != null) return;
            _menu = MenuRenderer.CreateMenu();
            _show = new ToolStripMenuItem("Show KeepAwake") { Font = Theme.Font(9.75f, FontStyle.Bold) };
            _show.Click += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
            _extend = new ToolStripMenuItem("Extend");
            foreach (var d in Durations.Extend)
            {
                var opt = d;
                var mi = new ToolStripMenuItem(opt.Label);
                mi.Click += (s, e) => ExtendRequested?.Invoke(this, opt);
                _extend.DropDownItems.Add(mi);
            }
            _deactivate = new ToolStripMenuItem("Deactivate");
            _deactivate.Click += (s, e) => DeactivateRequested?.Invoke(this, EventArgs.Empty);
            _options = new ToolStripMenuItem("Options");
            _optSimulate = OptionItem("Simulate activity", TrayOption.SimulateActivity);
            _optBattery = OptionItem("Stop when on battery", TrayOption.StopOnBattery);
            _optLocked = OptionItem("Stop when Windows is locked", TrayOption.StopWhenLocked);
            _options.DropDownItems.AddRange(new ToolStripItem[] { _optSimulate, _optBattery, _optLocked });
            _theme = new ToolStripMenuItem("Theme");
            foreach (ThemeMode m in Enum.GetValues(typeof(ThemeMode)))
            {
                var mode = m;
                var mi = new ToolStripMenuItem(mode.ToString()) { Tag = mode };
                mi.Click += (s, e) => ThemeRequested?.Invoke(this, mode);
                _theme.DropDownItems.Add(mi);
            }
            _quit = new ToolStripMenuItem("Quit") { Tag = "danger" };
            _quit.Click += (s, e) => QuitRequested?.Invoke(this, EventArgs.Empty);

            _menu.Items.AddRange(new ToolStripItem[]
            {
                _show, new ToolStripSeparator(), _extend, _deactivate, _options, _theme, new ToolStripSeparator(), _quit,
            });
            _menu.Opening += (s, e) => ApplyThemeToMenu();
            ApplyMenuState();
            ApplyOptionChecks();
            _icon.ContextMenuStrip = _menu;
        }

        private ToolStripMenuItem OptionItem(string text, TrayOption option)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += (s, e) => OptionRequested?.Invoke(this, option);
            return mi;
        }

        /// <summary>Sets the check marks of the Options submenu.</summary>
        public void SetOptions(bool simulate, bool onBattery, bool locked)
        {
            _simulateOn = simulate;
            _batteryOn = onBattery;
            _lockedOn = locked;
            ApplyOptionChecks();
        }

        private void ApplyOptionChecks()
        {
            if (_menu == null) return;
            _optSimulate.Checked = _simulateOn;
            _optBattery.Checked = _batteryOn;
            _optLocked.Checked = _lockedOn;
        }

        private static Icon LoadIcon(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                var size = SystemInformation.SmallIconSize;
                if (s == null) return new Icon(SystemIcons.Application, size.Width, size.Height);
                return new Icon(s, size.Width, size.Height);
            }
        }

        /// <summary>Refreshes icon, tooltip and menu states. Does nothing for a value that has not changed.</summary>
        public void Update(AwakeController c, ThemeMode theme)
        {
            if (_disposed) return;
            bool active = c.IsActive, forever = c.IsForever;
            var text = c.TooltipText();
            if (text.Length > 63) text = text.Substring(0, 63);

            if (!_hasState || active != _lastActive) _icon.Icon = active ? _active : _inactive;
            if (text != _lastText) _icon.Text = text;
            bool menuChanged = !_hasState || active != _lastActive || forever != _lastForever || theme != _lastTheme;

            _lastActive = active;
            _lastForever = forever;
            _lastTheme = theme;
            _lastText = text;
            _hasState = true;
            if (menuChanged) ApplyMenuState();
        }

        private void ApplyMenuState()
        {
            if (_menu == null || !_hasState) return;
            _extend.Enabled = _lastActive && !_lastForever;
            _deactivate.Enabled = _lastActive;
            ApplyOptionChecks();
            foreach (ToolStripMenuItem mi in _theme.DropDownItems)
                mi.Checked = (ThemeMode)mi.Tag == _lastTheme;
        }

        /// <summary>Shows a balloon. <paramref name="onClick"/> runs if the user clicks that balloon.</summary>
        public void Toast(string title, string text, Action onClick = null)
        {
            if (_disposed) return;
            _balloonAction = onClick;
            try { _icon.ShowBalloonTip(6000, title, text, ToolTipIcon.None); } catch { }
        }

        private void ApplyThemeToMenu()
        {
            var p = Theme.Current;
            _menu.BackColor = p.Menu;
            _menu.ForeColor = p.Text;
            foreach (ToolStripItem it in _menu.Items)
            {
                if (it is ToolStripDropDownItem d)
                {
                    d.DropDown.Renderer = _menu.Renderer;
                    d.DropDown.BackColor = p.Menu;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _icon.Visible = false;
            _icon.Dispose();
            _menu?.Dispose();
            _active.Dispose();
            _inactive.Dispose();
        }
    }
}
