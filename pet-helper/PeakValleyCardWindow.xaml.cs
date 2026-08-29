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
        PeriodLabel.FontSize = Math.Max(24d, placement.Height * 0.42d);

        if (!IsVisible) Show();
        closeTimer.Stop();
        closeTimer.Start();
    }

    public void Dismiss()
    {
        closeTimer.Stop();
        if (!isClosed && IsVisible) Hide();
    }

    public void CloseCard()
    {
        if (!isClosed) Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Dismiss();

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
