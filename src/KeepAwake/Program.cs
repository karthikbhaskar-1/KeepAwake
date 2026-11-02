using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using KeepAwake.Core;
using KeepAwake.UI;

namespace KeepAwake
{
    internal static class Program
    {
        private const string MutexName = @"Local\KeepAwake.SingleInstance";
        private const string ShowEventName = @"Local\KeepAwake.ShowWindow";

        [STAThread]
        private static void Main()
        {
            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first)
                {
                    // Another instance owns the tray icon: ask it to show its window and leave.
                    try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set(); } catch { }
                    return;
                }
                Run();
                GC.KeepAlive(mutex);
            }
        }

        private static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            var settingsPath = SettingsStore.ResolvePath(exeDir);
            var settings = Settings.Load(settingsPath);
            Theme.Apply(settings.ThemeMode);

            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            using (var controller = new AwakeController(SystemClock.Instance, new ThreadPowerHold()))
            using (var simulator = new ActivitySimulator(new MouseNudger(), TimeSpan.FromSeconds(60)))
            using (var tray = new TrayIcon())
            {
                var main = new MainForm(controller, simulator, settings, settingsPath, tray);

                var waiter = new Thread(() =>
                {
                    while (true)
                    {
                        try { showEvent.WaitOne(); } catch { return; }
                        try { main.BeginInvoke(new Action(main.ShowFromTray)); } catch { return; }
                    }
                })
                { IsBackground = true, Name = "KeepAwake.ShowWaiter" };
                waiter.Start();

                using (var splash = new SplashForm { Version = main.Version })
                {
                    splash.FormClosed += (s, e) => main.Show();
                    splash.Show();
                    Application.Run(new TrayApplicationContext(main));
                }
            }
        }

        /// <summary>Keeps the message loop alive while the main window is hidden in the tray.</summary>
        private sealed class TrayApplicationContext : ApplicationContext
        {
            public TrayApplicationContext(MainForm main)
            {
                // FormClosed only fires on a real close (Quit); hiding to the tray never closes the form.
                main.FormClosed += (s, e) => ExitThread();
            }
        }
    }
}
