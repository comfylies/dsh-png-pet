using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetHelper;

/// <summary>A non-activating, five-second peak/valley status card beside the pet.</summary>
public partial class PeakValleyCardWindow : Window
{
    private static readonly Uri CartoonFontUri = new("pack://application:,,,/pet-helper;component/Assets/Fonts/", UriKind.Absolute);
    private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool isClosed;
    private bool randomChatInvitationVisible;

    public event EventHandler? RandomChatClicked;

    public bool IsShowingRandomChatInvitation => randomChatInvitationVisible && IsVisible;

    public PeakValleyCardWindow()
    {
        InitializeComponent();
        TryApplyCartoonFont();
        closeTimer.Tick += (_, _) => Dismiss();
        Closed += (_, _) =>
        {
            isClosed = true;
            closeTimer.Stop();
        };
    }

    public void ShowPeriod(PeakValleyPeriod period, Rect headAnchor, IScreenLayout screenLayout, double headHeight)
    {
        if (isClosed) return;
        randomChatInvitationVisible = false;
        var placement = PeakValleyCardPlacement.Place(headAnchor, screenLayout.WorkAreaFor(headAnchor), headHeight);
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;

        PeriodLabel.Text = period == PeakValleyPeriod.Peak ? "梁文峰" : "梁文谷";
        PeriodLabel.Foreground = new SolidColorBrush(Color.FromRgb(
            period == PeakValleyPeriod.Peak ? (byte)217 : (byte)3,
            period == PeakValleyPeriod.Peak ? (byte)45 : (byte)152,
            period == PeakValleyPeriod.Peak ? (byte)32 : (byte)85));
        // The label is hosted in a DownOnly Viewbox, so an oversized glyph run shrinks
        // instead of being clipped; the size cap keeps the large text from dominating the
        // card at the bigger pet scales.
        PeriodLabel.FontSize = Math.Clamp(placement.Height * 0.42d, 24d, 30d);
        PeriodHost.Visibility = Visibility.Visible;
        RandomChatLabel.Visibility = Visibility.Collapsed;

        if (!IsVisible) Show();
        closeTimer.Stop();
        closeTimer.Start();
    }

    /// <summary>Shows a random-chat invitation in the same transient card used by a pet left click.</summary>
    public void ShowRandomChatInvitation(string text, string callToAction, Rect headAnchor, IScreenLayout screenLayout, double headHeight)
    {
        if (isClosed) return;
        randomChatInvitationVisible = true;
        var size = new Size(236d, Math.Max(104d, headHeight));
        var placement = PeakValleyCardPlacement.Place(headAnchor, screenLayout.WorkAreaFor(headAnchor), headHeight, size);
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        PeriodHost.Visibility = Visibility.Collapsed;
        RandomChatLabel.Visibility = Visibility.Visible;
        RandomChatLabel.Text = $"{text}\n{callToAction}";

        if (!IsVisible) Show();
        closeTimer.Stop();
    }

    public void ShowRandomChatError(Rect headAnchor, IScreenLayout screenLayout, double headHeight)
    {
        ShowRandomChatInvitation("暂时无法开始随机聊聊", "请稍后再试", headAnchor, screenLayout, headHeight);
        randomChatInvitationVisible = false;
    }

    public void Dismiss()
    {
        closeTimer.Stop();
        randomChatInvitationVisible = false;
        if (!isClosed && IsVisible) Hide();
    }

    public void CloseCard()
    {
        if (!isClosed) Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Dismiss();

    private void CardSurface_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!randomChatInvitationVisible) return;
        Dismiss();
        RandomChatClicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void TryApplyCartoonFont()
    {
        try
        {
            PeriodLabel.FontFamily = new FontFamily(CartoonFontUri, "./#ZCOOL KuaiLe");
        }
        catch
        {
            PeriodLabel.FontFamily = new FontFamily("Microsoft YaHei UI");
        }
    }
}
