using global::Windows.Win32;
using global::Windows.Win32.UI.Shell;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Lamha.Windows.Shell;

/// <summary>One item of the tray icon's menu; a null text is a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? Run = null)
{
    public static TrayMenuItem Separator { get; } = new((string?)null);
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
    private IReadOnlyList<TrayMenuItem> _menu = [];
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

    public void SetMenu(IReadOnlyList<TrayMenuItem> items) => _menu = items;

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
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            for (var i = 0; i < _menu.Count; i++)
            {
                var item = _menu[i];
                if (item.Text is null)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (char*)null);
                    continue;
                }

                fixed (char* text = item.Text)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)(i + 1), text);
                }
            }

            // The menu closes properly only when its owner is in front (a documented quirk of tray menus).
            PInvoke.SetForegroundWindow(_window.Handle);
            PInvoke.TrackPopupMenuEx(menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN), x, y, _window.Handle, null);
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    private void OnCommand(int id)
    {
        if (id >= 1 && id <= _menu.Count)
        {
            _menu[id - 1].Run?.Invoke();
        }
    }
}
