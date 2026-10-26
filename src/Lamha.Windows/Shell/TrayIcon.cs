using global::Windows.Win32;
using global::Windows.Win32.Graphics.Gdi;
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

    /// <summary>Shown in bold: what a click on the icon does.</summary>
    public bool Default { get; init; }

    /// <summary>One of a choice: with <see cref="Checked"/>, the menu's dot instead of a check mark.</summary>
    public bool Radio { get; init; }
}

/// <summary>
/// Lamha's icon in the notification area, with its menu. Plain Win32 (Shell_NotifyIcon), so it costs no window and
/// works with NativeAOT. Added again when Explorer restarts. The icon can carry a dot and be faded
/// (<see cref="TrayIconArt"/>), drawn at the tray's size.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    // shellapi.h: NIN_SELECT is WM_USER, NIN_KEYSELECT is NIN_SELECT | NINF_KEY.
    private const uint NinSelect = 0x400;
    private const uint NinKeySelect = 0x401;

    private readonly MessageWindow _window;
    private readonly HICON _icon;
    private readonly int _size;
    private HICON _marked; // the icon with its marks, while it has any
    private (bool Dot, bool Faded) _marks;
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
        // The tray's own size (16 px at 100%), so the marks are drawn on the pixels Windows shows.
        _size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        fixed (char* path = iconPath)
        {
            _icon = (HICON)(nint)PInvoke.LoadImage(
                default, path, GDI_IMAGE_TYPE.IMAGE_ICON, _size, _size, IMAGE_FLAGS.LR_LOADFROMFILE).Value;
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

    /// <summary>The icon with a dot (words waiting for review) and faded (Lamha turned off), or as it is.</summary>
    public void SetMarks(bool dot, bool faded)
    {
        if (_marks == (dot, faded) || _icon.IsNull)
        {
            return;
        }

        _marks = (dot, faded);
        var old = _marked;
        _marked = dot || faded ? Draw(dot, faded) : default;
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_ICON);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        if (!old.IsNull)
        {
            PInvoke.DestroyIcon(old);
        }
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_TIP);
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

        if (!_marked.IsNull)
        {
            PInvoke.DestroyIcon(_marked);
        }
    }

    private void Add()
    {
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON);
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
            // With NOTIFYICON_VERSION_4, any change sent without NIF_SHOWTIP hides the tooltip: it goes with every one.
            uFlags = flags == 0 ? 0 : flags | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP,
            uCallbackMessage = MessageWindow.TrayCallback,
            hIcon = _marked.IsNull ? _icon : _marked,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(new Span<char>(&data.szTip, 128));
        return data;
    }

    /// <summary>A copy of the icon with its marks: its pixels read, marked, and made into a new icon.</summary>
    private HICON Draw(bool dot, bool faded)
    {
        ICONINFO info;
        if (!PInvoke.GetIconInfo(_icon, &info))
        {
            return default;
        }

        var header = new BITMAPINFO();
        header.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        header.bmiHeader.biWidth = _size;
        header.bmiHeader.biHeight = -_size; // top row first
        header.bmiHeader.biPlanes = 1;
        header.bmiHeader.biBitCount = 32;
        var pixels = new byte[_size * _size * 4];
        var screen = PInvoke.GetDC(default);
        try
        {
            fixed (byte* p = pixels)
            {
                _ = PInvoke.GetDIBits(screen, info.hbmColor, 0, (uint)_size, p, &header, DIB_USAGE.DIB_RGB_COLORS);
            }

            if (dot)
            {
                TrayIconArt.Dot(pixels, _size);
            }

            // After the dot, so it fades too (the design's filter is on the whole icon).
            if (faded)
            {
                TrayIconArt.Fade(pixels);
            }

            void* bits;
            var colour = PInvoke.CreateDIBSection(screen, &header, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
            pixels.CopyTo(new Span<byte>(bits, pixels.Length));
            var mask = PInvoke.CreateBitmap(_size, _size, 1, 1, null); // nothing masked: the alpha decides
            var made = new ICONINFO { fIcon = true, hbmColor = colour, hbmMask = mask };
            var icon = PInvoke.CreateIconIndirect(&made);
            PInvoke.DeleteObject(colour);
            PInvoke.DeleteObject(mask);
            return icon;
        }
        finally
        {
            _ = PInvoke.ReleaseDC(default, screen);
            PInvoke.DeleteObject(info.hbmColor);
            PInvoke.DeleteObject(info.hbmMask);
        }
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
        for (var place = 0; place < items.Count; place++)
        {
            var item = items[place];
            if (item.Text is null)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (char*)null);
                continue;
            }

            var flags = MENU_ITEM_FLAGS.MF_STRING
                | (item.Checked && !item.Radio ? MENU_ITEM_FLAGS.MF_CHECKED : 0)
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

            if (item.Radio && item.Checked)
            {
                // The choice made gets the menu's dot, and the radio item's look.
                PInvoke.CheckMenuRadioItem(menu, (uint)place, (uint)place, (uint)place, (uint)MENU_ITEM_FLAGS.MF_BYPOSITION);
            }

            if (item.Default)
            {
                PInvoke.SetMenuDefaultItem(menu, (uint)place, 1); // by position
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
