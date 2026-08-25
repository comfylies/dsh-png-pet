using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace PetHelper;

public partial class App : System.Windows.Application
{
    private bool shutdownRequested;
    private PetTrayIcon? trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureWindowsDirectoryEnvironment();
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        var tray = new PetTrayIcon(
            () => Dispatcher.Invoke(ShowMainWindow),
            () => Dispatcher.Invoke(ExitFromTray));
        trayIcon = tray;
        window.HiddenToTray += (_, _) => tray.Show();
        window.Show();

        Console.Out.WriteLine(SerializeHelperMessage("ready"));
        Console.Out.Flush();
        _ = Task.Run(ReadProtocolLoop);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (shutdownRequested)
        {
            Console.Out.WriteLine(SerializeHelperMessage("closed"));
            Console.Out.Flush();
        }

        trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.Activate();
    }

    private void ExitFromTray() => Shutdown();

    private async Task ReadProtocolLoop()
    {
        try
        {
            using var reader = Console.In;
            while (await reader.ReadLineAsync() is { } line)
            {
                var message = ProtocolReader.Parse(line);
                switch (message)
                {
                    case HelloMessage:
                        continue;
                    case ConfigMessage config:
                        await Dispatcher.InvokeAsync(() => ((MainWindow)MainWindow!).ApplyConfig(config));
                        continue;
                    case StateMessage state:
                        await Dispatcher.InvokeAsync(() => ((MainWindow)MainWindow!).ApplyDisplayState(PetDisplayState.From(state.State, state.Activities, state.Label, state.Sequence)));
                        continue;
                    case ShutdownMessage:
                        shutdownRequested = true;
                        await Dispatcher.InvokeAsync(Shutdown);
                        return;
                    default:
                        shutdownRequested = true;
                        await Dispatcher.InvokeAsync(ShowDisconnectedThenShutdown);
                        return;
                }
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"pet-helper protocol loop fault: {error.GetType().Name}");
            Console.Error.Flush();
            shutdownRequested = true;
            await Dispatcher.InvokeAsync(ShowDisconnectedThenShutdown);
        }
    }

    private void ShowDisconnectedThenShutdown()
    {
        if (MainWindow is MainWindow window)
        {
            window.ApplyDisplayState(PetDisplayState.Disconnected);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Shutdown();
        };
        timer.Start();
    }

    private static string SerializeHelperMessage(string kind) =>
        JsonSerializer.Serialize(new { version = 3, kind });

    private static void EnsureWindowsDirectoryEnvironment()
    {
        if (!OperatingSystem.IsWindows() || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
        {
            return;
        }

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot) && Directory.Exists(systemRoot))
        {
            Environment.SetEnvironmentVariable("WINDIR", systemRoot);
        }
    }
}
