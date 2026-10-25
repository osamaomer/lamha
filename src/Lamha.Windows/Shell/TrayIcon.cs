using global::Windows.Win32;
using global::Windows.Win32.UI.Shell;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Lamha.Windows.Shell;

/// <summary>One item of the tray icon's menu; a null text is a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? Run = null)
{
    public static TrayMenuItem Separator { get; } = new((string?)null);

    /// <summary>Shown with a check mark (a setting that's on, or the choice made).</summary>
    public bool Checked { get; init; }

    /// <summary>False: shown greyed out (information, such as the shortcuts).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>A submenu's items; the item itself then does nothing.</summary>
    public IReadOnlyList<TrayMenuItem>? Items { get; init; }
}

/// <summary>
/// Lamha's icon in the notification area, with its menu. Plain Win32 (Shell_NotifyIcon), so it costs no window and
/// works with NativeAOT. Added again when Explorer restarts.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    // shellapi.h: NIN_SELECT is WM_USER, NIN_KEYSELECT is NIN_SELECT | NINF_KEY.
    private const uint NinSelect = 0x400;
    private const uint NinKeySelect = 0x401;

    private readonly MessageWindow _window;
    private readonly HICON _icon;
    private string _tooltip;
    private Func<IReadOnlyList<TrayMenuItem>> _menu = () => [];
    private List<TrayMenuItem> _shown = [];
    private bool _added;

    /// <param name="window">Receives the icon's clicks.</param>
    /// <param name="iconPath">An .ico file.</param>
    /// <param name="tooltip">The name shown on hover.</param>
    public TrayIcon(MessageWindow window, string iconPath, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        fixed (char* path = iconPath)
        {
            _icon = (HICON)(nint)PInvoke.LoadImage(
                default, path, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0,
                IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE).Value;
        }

        _window.TrayMessage += OnTrayMessage;
        _window.TaskbarCreated += Add;
        _window.Command += OnCommand;
        Add();
    }

    /// <summary>A left click (or Enter on the icon).</summary>
    public event Action? Invoked;

    /// <summary>The menu, built each time it opens (so checks and counts are current).</summary>
    public void SetMenu(Func<IReadOnlyList<TrayMenuItem>> build) => _menu = build;

    /// <summary>A notification from the icon (Windows 11 shows it as a toast). Texts are cut to Windows' limits.</summary>
    public void Notify(string title, string body)
    {
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        title.AsSpan(0, Math.Min(title.Length, 63)).CopyTo(new Span<char>(&data.szInfoTitle, 64));
        body.AsSpan(0, Math.Min(body.Length, 255)).CopyTo(new Span<char>(&data.szInfo, 256));
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_USER | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_LARGE_ICON;
        data.hBalloonIcon = _icon;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
    }

    public void Dispose()
    {
        _window.TrayMessage -= OnTrayMessage;
        _window.TaskbarCreated -= Add;
        _window.Command -= OnCommand;
        if (_added)
        {
            var data = Data(0);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            _added = false;
        }

        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
        }
    }

    private void Add()
    {
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        _added = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW Data(NOTIFY_ICON_DATA_FLAGS flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = MessageWindow.TrayCallback,
            hIcon = _icon,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(new Span<char>(&data.szTip, 128));
        return data;
    }

    private void OnTrayMessage(uint message, int x, int y)
    {
        switch (message)
        {
            case PInvoke.WM_LBUTTONUP:
            case NinSelect:
            case NinKeySelect:
                Invoked?.Invoke();
                break;
            case PInvoke.WM_CONTEXTMENU:
            case PInvoke.WM_RBUTTONUP:
                ShowMenu(x, y);
                break;
        }
    }

    private void ShowMenu(int x, int y)
    {
        _shown = [];
        var menu = Build(_menu());
        try
        {
            // The menu closes properly only when its owner is in front (a documented quirk of tray menus).
            PInvoke.SetForegroundWindow(_window.Handle);
            PInvoke.TrackPopupMenuEx(menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN), x, y, _window.Handle, null);
        }
        finally
        {
            PInvoke.DestroyMenu(menu); // and its submenus with it
        }
    }

    /// <summary>A popup menu for <paramref name="items"/>; each item's id is its place in <see cref="_shown"/> + 1.</summary>
    private HMENU Build(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = PInvoke.CreatePopupMenu();
        foreach (var item in items)
        {
            if (item.Text is null)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (char*)null);
                continue;
            }

            var flags = MENU_ITEM_FLAGS.MF_STRING
                | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : 0)
                | (item.Enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED);
            nuint id;
            if (item.Items is { } children)
            {
                flags |= MENU_ITEM_FLAGS.MF_POPUP;
                id = (nuint)(nint)Build(children).Value;
            }
            else
            {
                _shown.Add(item);
                id = (nuint)_shown.Count;
            }

            fixed (char* text = item.Text)
            {
                PInvoke.AppendMenu(menu, flags, id, text);
            }
        }

        return menu;
    }

    private void OnCommand(int id)
    {
        if (id >= 1 && id <= _shown.Count)
        {
            _shown[id - 1].Run?.Invoke();
        }
    }
}
