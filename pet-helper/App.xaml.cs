using System.IO;
using System.Windows;

namespace PetHelper;

public partial class App : Application
{
    private bool shutdownRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureWindowsDirectoryEnvironment();
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        Console.Out.WriteLine("{\"version\":1,\"kind\":\"ready\"}");
        Console.Out.Flush();
        _ = Task.Run(ReadProtocolLoop);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (shutdownRequested)
        {
            Console.Out.WriteLine("{\"version\":1,\"kind\":\"closed\"}");
            Console.Out.Flush();
        }

        base.OnExit(e);
    }

    private async Task ReadProtocolLoop()
    {
        using var reader = Console.In;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (ProtocolReader.Parse(line) is not { Kind: "shutdown" }) continue;
            shutdownRequested = true;
            await Dispatcher.InvokeAsync(Shutdown);
            return;
        }
    }

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
