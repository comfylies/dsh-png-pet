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
    private DialogueWindow? dialogue;
    private TargetWindow? targetWindow;
    private PeakValleyCardWindow? peakValleyCard;

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureWindowsDirectoryEnvironment();
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
        var screenLayout = new Win32ScreenLayout();
        var window = new MainWindow(screenLayout);
        MainWindow = window;
        var dialogueWindow = new DialogueWindow(screenLayout);
        dialogue = dialogueWindow;
        dialogueWindow.InputSubmitted += (_, input) => WriteInput(input);
        dialogueWindow.HistoryRequested += (_, request) => WriteHistoryRequest(request);
        dialogueWindow.StopRequested += (_, stop) => WriteStop(stop);
        window.AttachDialogueWindow(dialogueWindow);

        var priceCard = new PeakValleyCardWindow();
        peakValleyCard = priceCard;
        window.AttachPeakValleyCard(priceCard);

        var target = new TargetWindow();
        targetWindow = target;
        target.TargetOpenRequested += (_, args) => WriteTargetOpen(args.RequestId);
        target.TargetAnswered += (_, args) => WriteTargetAnswer(args);
        window.TargetSelectionRequested += (_, droppedPath) => target.ShowCard(droppedPath);

        var tray = new PetTrayIcon(
            () => Dispatcher.Invoke(ShowMainWindow),
            () => Dispatcher.Invoke(ExitFromTray));
        trayIcon = tray;
        window.HiddenToTray += (_, _) => tray.Show();
        dialogueWindow.HiddenToTray += (_, _) => tray.Show();
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

        dialogue?.SaveState();
        (MainWindow as MainWindow)?.SaveState();
        peakValleyCard?.CloseCard();
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

    private void ExitFromTray()
    {
        dialogue?.SaveState();
        Shutdown();
    }

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
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ((MainWindow)MainWindow!).ApplyConfig(config);
                            dialogue?.ApplyConfig(config);
                        });
                        continue;
                    case StateMessage state:
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ((MainWindow)MainWindow!).ApplyDisplayState(PetDisplayState.From(state.State, state.Activities, state.Label, state.Sequence));
                            dialogue?.ApplyPetState(state);
                        });
                        continue;
                    case ConversationConfigMessage or InputStatusMessage or ReplyPreviewMessage or ClearPreviewMessage or ReplyMessage or HistoryMessage:
                        await Dispatcher.InvokeAsync(() => dialogue?.ApplyConversationMessage(message));
                        continue;
                    case TargetRequestMessage target:
                        await Dispatcher.InvokeAsync(() => targetWindow?.ApplyTargetRequest(target));
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

    /// <summary>
    /// A transient UI fault (render, layout, clipboard) must never kill the pet:
    /// log the details and continue. The log is diagnostic-only and never carries content.
    /// </summary>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DshPngPet",
                "pet-helper-errors.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:O} {e.Exception}\n");
        }
        catch
        {
            // Logging must never throw again inside the fault handler.
        }
        e.Handled = true;
    }

    private static string SerializeHelperMessage(string kind) =>
        JsonSerializer.Serialize(new { version = ProtocolMessage.ProtocolVersion, kind });

    private static void WriteInput(InputSubmittedEventArgs input)
    {
        var payload = new Dictionary<string, object?>
        {
            ["version"] = ProtocolMessage.ProtocolVersion,
            ["kind"] = "input",
            ["requestId"] = input.RequestId,
            ["text"] = input.Text,
        };
        if (!input.Attachments.IsEmpty)
        {
            payload["attachments"] = input.Attachments
                .Select(attachment => attachment switch
                {
                    ImageInputAttachment image => (object)new
                    {
                        type = "image",
                        mediaType = image.MediaType,
                        base64 = image.Base64,
                        name = image.Name,
                    },
                    FileInputAttachment file => new
                    {
                        type = "file",
                        path = file.Path,
                        name = file.Name,
                    },
                    _ => throw new InvalidOperationException("unknown attachment kind"),
                })
                .ToArray();
        }
        Console.Out.WriteLine(JsonSerializer.Serialize(payload));
        Console.Out.Flush();
    }

    private static void WriteHistoryRequest(HistoryRequestedEventArgs request)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new { version = ProtocolMessage.ProtocolVersion, kind = "request-history", requestId = request.RequestId }));
        Console.Out.Flush();
    }

    private static void WriteStop(StopRequestedEventArgs stop)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new { version = ProtocolMessage.ProtocolVersion, kind = "stop", requestId = stop.RequestId }));
        Console.Out.Flush();
    }

    private static void WriteTargetOpen(long requestId)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new { version = ProtocolMessage.ProtocolVersion, kind = "target-open", requestId }));
        Console.Out.Flush();
    }

    private static void WriteTargetAnswer(TargetAnswerEventArgs answer)
    {
        var payload = new Dictionary<string, object?>
        {
            ["version"] = ProtocolMessage.ProtocolVersion,
            ["kind"] = "target-answer",
            ["requestId"] = answer.RequestId,
            ["sessionId"] = answer.SessionId,
            ["workspaceId"] = answer.WorkspaceId,
            ["newBlank"] = answer.NewBlank,
        };
        if (answer.NewWorkspace)
        {
            payload["newWorkspace"] = true;
            payload["path"] = answer.Path;
        }
        Console.Out.WriteLine(JsonSerializer.Serialize(payload));
        Console.Out.Flush();
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

