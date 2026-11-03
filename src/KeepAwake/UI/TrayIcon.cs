using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeepAwake.Core;

namespace KeepAwake.UI
{
    /// <summary>Notification-area icon, tooltip, balloon toast and the right-click menu.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Icon _active, _inactive;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _show, _extend, _deactivate, _theme, _quit;

        private bool _lastActive, _lastForever;
        private ThemeMode _lastTheme = ThemeMode.System;
        private string _lastText = "KeepAwake - Inactive";
        private bool _hasState, _disposed;

        public event EventHandler ShowRequested;
        public event EventHandler<DurationOption> ExtendRequested;
        public event EventHandler DeactivateRequested;
        public event EventHandler<ThemeMode> ThemeRequested;
        public event EventHandler QuitRequested;

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
            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty); };
            _icon.DoubleClick += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
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
                _show, new ToolStripSeparator(), _extend, _deactivate, _theme, new ToolStripSeparator(), _quit,
            });
            _menu.Opening += (s, e) => ApplyThemeToMenu();
            ApplyMenuState();
            _icon.ContextMenuStrip = _menu;
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
            foreach (ToolStripMenuItem mi in _theme.DropDownItems)
                mi.Checked = (ThemeMode)mi.Tag == _lastTheme;
        }

        public void Toast(string title, string text)
        {
            if (_disposed) return;
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
