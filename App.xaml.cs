using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SaveSpace.Services;

namespace SaveSpace
{
    public partial class App : Application
    {
        static App()
        {
            // Vangt fouten die ontstaan voordat OnStartup draait (bijvoorbeeld in de XAML).
            AppDomain.CurrentDomain.UnhandledException += (_, args) => StartupLog.Fail(args.ExceptionObject);
        }

        public App()
        {
            StartupLog.Mark("App-constructor klaar");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            StartupLog.Mark("OnStartup gestart");
            base.OnStartup(e);
            try { SmoothScroll.Enable(); }
            catch (Exception ex) { StartupLog.Fail("SmoothScroll: " + ex); }
            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                AppPaths.Log("Onverwachte fout: " + args.ExceptionObject);

            try
            {
                AppServices.Initialize();
                StartupLog.Mark("Services geladen");
                var themeArg = e.Args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));
                if (themeArg != null)
                {
                    var value = themeArg["--theme=".Length..].Trim().ToLowerInvariant();
                    if (value is "dark" or "light")
                    {
                        AppServices.Settings.Theme = value;
                        ThemeManager.Apply(AppServices.Settings);
                    }
                }
                var window = new MainWindow();
                StartupLog.Mark("Hoofdvenster aangemaakt");
                MainWindow = window;
                window.Show();
                StartupLog.Mark("Hoofdvenster getoond");
                var notePackage = e.Args.FirstOrDefault(a =>
                    !a.StartsWith("--", StringComparison.Ordinal) &&
                    string.Equals(Path.GetExtension(a), ".noteagent", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(notePackage))
                    Dispatcher.BeginInvoke(new Action(() => window.OpenNotePackagePath(notePackage)));
                _ = Task.Run(() =>
                {
                    try { AppServices.Backups.RunAutomaticIfDue(); }
                    catch (Exception ex) { AppPaths.Log("Auto-back-up: " + ex.Message); }
                });
            }
            catch (Exception ex)
            {
                AppPaths.Log("Opstarten mislukt: " + ex);
                StartupLog.Fail(ex);
                MessageBox.Show(
                    "SaveSpace kon niet starten.\n\n" + ex.Message +
                    "\n\nDe fout is gelogd in:\n" + AppPaths.LogFile,
                    "SaveSpace", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppPaths.Log("UI-fout: " + e.Exception);
            StartupLog.Fail(e.Exception);
            MessageBox.Show(
                "Er ging iets mis, maar SaveSpace blijft open.\n\n" + e.Exception.Message,
                "SaveSpace", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
