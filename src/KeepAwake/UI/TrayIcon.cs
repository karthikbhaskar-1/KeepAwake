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
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _show, _extend, _deactivate, _theme, _quit;

        public event EventHandler ShowRequested;
        public event EventHandler<DurationOption> ExtendRequested;
        public event EventHandler DeactivateRequested;
        public event EventHandler<ThemeMode> ThemeRequested;
        public event EventHandler QuitRequested;

        public TrayIcon()
        {
            _active = LoadIcon("tray_active.ico");
            _inactive = LoadIcon("tray_inactive.ico");

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

            _icon = new NotifyIcon
            {
                Icon = _inactive,
                Text = "KeepAwake - Inactive",
                Visible = true,
                ContextMenuStrip = _menu,
            };
            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty); };
            _icon.DoubleClick += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
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

        public void Update(AwakeController c, ThemeMode theme)
        {
            _icon.Icon = c.IsActive ? _active : _inactive;
            var text = c.TooltipText();
            _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            _extend.Enabled = c.IsActive && !c.IsForever;
            _deactivate.Enabled = c.IsActive;
            foreach (ToolStripMenuItem mi in _theme.DropDownItems)
                mi.Checked = (ThemeMode)mi.Tag == theme;
        }

        public void Toast(string title, string text)
        {
            try { _icon.ShowBalloonTip(6000, title, text, ToolTipIcon.None); } catch { }
        }

        private void ApplyThemeToMenu()
        {
            var p = Theme.Current;
            _menu.BackColor = p.Menu;
            _menu.ForeColor = p.Text;
            foreach (ToolStripItem it in _menu.Items)
            {
                it.ForeColor = p.Text;
                if (it is ToolStripDropDownItem d)
                {
                    d.DropDown.Renderer = _menu.Renderer;
                    d.DropDown.BackColor = p.Menu;
                    foreach (ToolStripItem sub in d.DropDownItems) sub.ForeColor = p.Text;
                }
            }
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _active.Dispose();
            _inactive.Dispose();
        }
    }
}
