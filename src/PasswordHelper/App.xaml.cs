using System;
using System.Windows;
using System.Windows.Threading;
using PasswordHelper.Core;

namespace PasswordHelper;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CommandLineArgs args = CommandLineArgs.Parse(e.Args);
        AppPaths.Initialize(args.DataDirectory);
        Log.Initialize(AppPaths.LogDirectory);

#if DEBUG
        if (args.SeedDemo)
        {
            _ = DevSeed.RunThenExitAsync(this);
            return;
        }

        if (args.ThemeProbe)
        {
            DevThemeProbe.Run();
            Shutdown();
            return;
        }
#endif

        _instance = SingleInstance.TryAcquire(AppPaths.InstanceKey);
        if (_instance is null)
        {
            // Another instance already owns this data directory in this session: forward the request and leave.
            InstanceCommand command = args.Command ?? InstanceCommand.Show;
            bool delivered = SingleInstance.Signal(AppPaths.InstanceKey, command);
            Log.Info($"Second instance forwarded '{command}' to the running instance (delivered={delivered}).");
            Shutdown();
            return;
        }

        if (args.Command == InstanceCommand.Exit)
        {
            // Asked to exit, but we are the only instance: nothing to do.
            _instance.Dispose();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        Log.Info($"Password helper starting. {SystemInfo.Describe()}");

        try
        {
            _host = new AppHost(this, args);
            _instance.Listen(cmd => Dispatcher.BeginInvoke(new Action(() => _host?.HandleInstanceCommand(cmd))));
            _host.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error during startup", ex);
            MessageBox.Show(
                "Password helper could not start.\n\n" + ex.Message,
                "Password helper", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Dispose();
            _instance?.Dispose();
            Log.Info("Password helper exited.");
        }
        catch (Exception ex)
        {
            Log.Error("Error during shutdown", ex);
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the tray app alive on non-fatal UI errors; the details go to the log, never the user's secrets.
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled application exception", e.ExceptionObject as Exception);
    }
}
