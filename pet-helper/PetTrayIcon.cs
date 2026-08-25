using System.Drawing;
using System.Windows.Forms;

namespace PetHelper;

public sealed class PetTrayIcon : IDisposable
{
    private readonly ContextMenuStrip menu;
    private readonly NotifyIcon notifyIcon;

    public PetTrayIcon(Action showPet, Action exitPet)
    {
        menu = new ContextMenuStrip();
        menu.Items.Add("显示桌宠", null, (_, _) => showPet());
        menu.Items.Add("退出桌宠", null, (_, _) => exitPet());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DSH PNG 桌宠",
            ContextMenuStrip = menu,
            Visible = false,
        };
        notifyIcon.DoubleClick += (_, _) => showPet();
    }

    public void Show() => notifyIcon.Visible = true;

    public void Dispose()
    {
        notifyIcon.Dispose();
        menu.Dispose();
    }
}
