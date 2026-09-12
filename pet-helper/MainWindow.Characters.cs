using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PetHelper;

public partial class MainWindow
{
    private readonly CharacterLibrary characterLibrary = new();
    private readonly SemaphoreSlim characterSwitchGate = new(1, 1);
    private readonly DispatcherTimer characterNoticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private CharacterWindow? characterWindow;
    private string? currentCharacterId;
    private double currentCharacterBaseline = 0.976;
    private int characterGeneration;
    private bool characterClosing;

    private void CharacterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (characterWindow is not null) { characterWindow.Activate(); return; }
        characterWindow = new CharacterWindow(characterLibrary, id => UseCharacterAsync(id), () => currentCharacterId) { Owner = this };
        characterWindow.Closed += (_, _) => characterWindow = null;
        characterWindow.Show();
    }

    private async Task RestoreCharacterAsync()
    {
        if (characterGeneration != 0 || characterClosing) return;
        var id = await Task.Run(() => characterLibrary.SelectedId);
        if (characterGeneration != 0 || characterClosing || id is null) return;
        if (!await UseCharacterAsync(id, persist: false)) ShowCharacterNotice("人物素材不可用，已使用默认人物。");
    }

    private async Task<bool> UseCharacterAsync(string? id, bool persist = true)
    {
        var generation = ++characterGeneration;
        await characterSwitchGate.WaitAsync();
        PetAnimationPlayer? candidate = null;
        var previousId = currentCharacterId;
        var saved = false;
        var applied = false;
        try
        {
            if (characterClosing || generation != characterGeneration) return false;
            var source = id is null ? null : await Task.Run(() => characterLibrary.Load(id));
            if (characterClosing || generation != characterGeneration) return false;
            candidate = new PetAnimationPlayer(new Image(), source);
            candidate.Apply(lastDisplayState.AnimationKey, reducedMotion);
            candidate.Pause();
            if (candidate.HasFailed) return false;
            if (persist)
            {
                await Task.Run(() => characterLibrary.Select(id));
                saved = true;
            }
            if (characterClosing || generation != characterGeneration) return false;
            // State may have advanced while loading or saving; prepare its latest first frame.
            candidate.Apply(lastDisplayState.AnimationKey, reducedMotion);
            if (candidate.HasFailed) return false;
            PausePhysics();
            var oldFoot = PetImage.ActualHeight * currentCharacterBaseline;
            var newBaseline = source?.Document.Baseline ?? 0.976;
            animationPlayer.Stop();
            candidate.AttachImage(PetImage);
            animationPlayer = candidate;
            candidate = null;
            currentCharacterId = id;
            currentCharacterBaseline = newBaseline;
            applied = true;
            if (id is not null) animationPlayer.AssetFailed += CharacterAssetFailed;
            if (double.IsFinite(Top)) Top += oldFoot - PetImage.ActualHeight * newBaseline;
            ClampPetIntoProtrusion();
            UpdateStateBubblePosition();
            if (dragging) animationPlayer.Pause();
            return true;
        }
        catch { return false; }
        finally
        {
            candidate?.Stop();
            if (saved && !applied)
            {
                try { await Task.Run(() => characterLibrary.Select(previousId)); } catch { }
            }
            characterSwitchGate.Release();
        }
    }

    private void CharacterAssetFailed(object? sender, EventArgs e)
    {
        // Leave the playback tick before replacing its context.
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (characterClosing || !ReferenceEquals(sender, animationPlayer)) return;
            if (await UseCharacterAsync(null, persist: false))
            {
                // Selection writes use the switch gate too; do not overwrite a later user choice.
                await characterSwitchGate.WaitAsync();
                try { if (currentCharacterId is null) await Task.Run(() => characterLibrary.Select(null)); }
                catch { }
                finally { characterSwitchGate.Release(); }
                ShowCharacterNotice("人物素材不可用，已恢复默认人物。");
            }
        }));
    }

    private void ShowCharacterNotice(string message)
    {
        if (characterClosing) return;
        characterWindow?.ShowNotice(message);
        CharacterToastText.Text = message;
        CharacterToast.Visibility = Visibility.Visible;
        characterNoticeTimer.Stop();
        characterNoticeTimer.Tick -= HideCharacterNotice;
        characterNoticeTimer.Tick += HideCharacterNotice;
        characterNoticeTimer.Start();
    }

    private void HideCharacterNotice(object? sender, EventArgs e)
    {
        characterNoticeTimer.Stop();
        CharacterToast.Visibility = Visibility.Collapsed;
    }

    private void CloseCharacterLibrary()
    {
        characterClosing = true;
        characterGeneration++;
        characterNoticeTimer.Stop();
        characterWindow?.Close();
    }
}
