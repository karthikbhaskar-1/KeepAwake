using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeepAwake.Core;
using KeepAwake.Native;
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
                    SignalFirstInstance();
                    return;
                }
                // Create the signal event before anything slow, so a second launch right now still finds it.
                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Run(showEvent);
                }
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>Another instance owns the tray icon: let it take the foreground, ask it to show its window, and leave.</summary>
        private static void SignalFirstInstance()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                        ev.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
                catch { return; }
            }
        }

        private static void Run(EventWaitHandle showEvent)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Trace.WriteLine(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Trace.WriteLine(e.ExceptionObject);

            var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            var settingsPath = SettingsStore.ResolvePath(exeDir);
            var settings = Settings.Load(settingsPath);
            Theme.Dpi = WindowChrome.DpiAtCursor();
            Theme.Apply(settings.ThemeMode);

            using (var splash = new SplashForm { Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3) })
            {
                // First pixel: nothing else runs before the splash is on screen.
                splash.Show();
                splash.Update();

                // Disposal order is the reverse of declaration: controller and simulator go before the tray icon,
                // so nothing can raise an event into a disposed NotifyIcon.
                using (var tray = new TrayIcon())
                using (var controller = new AwakeController(SystemClock.Instance, new ThreadPowerHold()))
                using (var simulator = new ActivitySimulator(new MouseNudger(), TimeSpan.FromSeconds(60)))
                {
                    var main = new MainForm(controller, simulator, settings, settingsPath, tray);
                    main.Prewarm();
                    StartWaiter(showEvent, main);
                    splash.FormClosed += (s, e) => { if (!main.IsDisposed) main.Show(); };
                    splash.StartTimeline();
                    DeferMenuBuild(main);
                    Application.Run(new TrayApplicationContext(main));
                }
            }
        }

        /// <summary>Builds the menus once the splash fade-in is over, so the work does not stutter the fade.</summary>
        private static void DeferMenuBuild(MainForm main)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 400 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                main.PrepareMenus();
            };
            timer.Start();
        }

        private static void StartWaiter(EventWaitHandle showEvent, MainForm main)
        {
            var waiter = new Thread(() =>
            {
                while (true)
                {
                    try { showEvent.WaitOne(); } catch { return; }
                    try { main.BeginInvoke(new Action(main.ShowFromTray)); }
                    catch (ObjectDisposedException) { return; }
                    catch (InvalidOperationException) { /* handle not ready or already gone: keep listening */ if (main.IsDisposed) return; }
                }
            }, 64 * 1024)
            { IsBackground = true, Name = "KeepAwake.ShowWaiter" };
            waiter.Start();
        }

        /// <summary>Keeps the message loop alive while the main window is hidden in the tray.</summary>
        private sealed class TrayApplicationContext : ApplicationContext
        {
            public TrayApplicationContext(MainForm main)
            {
                // Hiding to the tray never closes the form; a real close or Quit ends the loop.
                main.FormClosed += (s, e) => ExitThread();
                main.ExitRequested += (s, e) => ExitThread();
            }
        }
    }
}
