using System.Windows.Input;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ComposerInputTests
{
    [Theory]
    [InlineData(Key.Enter, ModifierKeys.None, 1)]
    [InlineData(Key.Enter, ModifierKeys.Shift, 2)]
    [InlineData(Key.Enter, ModifierKeys.Control, 0)]
    [InlineData(Key.A, ModifierKeys.None, 0)]
    public void Maps_common_ai_composer_shortcuts(Key key, ModifierKeys modifiers, int expected)
    {
        Assert.Equal((ComposerInputAction)expected, DialogueWindow.ComposerInputActionFor(key, modifiers));
    }
}
