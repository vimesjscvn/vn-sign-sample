using System.Drawing;
using System.Runtime.InteropServices;

namespace VMSign.AppE2E;

internal static class WindowsUiDriver
{
    private const int IdOk = 1;
    private const uint WmSetText = 0x000C;
    private const uint WmCommand = 0x0111;
    private const uint BmClick = 0x00F5;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    public static void PlaceMainWindowForScreenshots(int processId)
    {
        var mainWindow = WaitForWindow(
            className: null,
            windowName: "Vimes SignSDK Showcase Studio",
            processId,
            TimeSpan.FromSeconds(5));

        // The test desktop uses 125% scaling. The XAML 1280x850 logical size
        // otherwise exceeds the physical work area and screen capture includes
        // pixels from the desktop behind the window.
        if (!MoveWindow(mainWindow, 0, 0, 1280, 800, repaint: true))
        {
            throw new InvalidOperationException(
                $"Could not place the VMSign window for E2E screenshots (Win32 {Marshal.GetLastWin32Error()}).");
        }

        SetForegroundWindow(mainWindow);
        Thread.Sleep(250);
    }

    public static float GetMainWindowDpiScale(int processId)
    {
        var mainWindow = WaitForWindow(
            className: null,
            windowName: "Vimes SignSDK Showcase Studio",
            processId,
            TimeSpan.FromSeconds(5));
        var dpi = GetDpiForWindow(mainWindow);
        return dpi == 0 ? 1F : dpi / 96F;
    }

    public static void SelectPdfFromOpenDialog(int processId, string pdfPath)
    {
        var dialog = WaitForWindow("#32770", "Select PDF Document", processId, TimeSpan.FromSeconds(10));

        // Standard Windows Open dialog hierarchy:
        // ComboBoxEx32 (1148) -> ComboBox -> Edit.
        var comboBoxEx = GetDlgItem(dialog, 1148);
        var comboBox = FindWindowEx(comboBoxEx, IntPtr.Zero, "ComboBox", null);
        var edit = FindWindowEx(comboBox, IntPtr.Zero, "Edit", null);
        var openButton = GetDlgItem(dialog, 1);

        EnsureHandle(comboBoxEx, "file name ComboBoxEx32");
        EnsureHandle(comboBox, "file name ComboBox");
        EnsureHandle(edit, "file name Edit");
        EnsureHandle(openButton, "Open button");

        // OpenFilePicker is configured for multi-select. Quoting the absolute
        // name uses the shell dialog's canonical multi-select input format.
        var quotedPath = $"\"{Path.GetFullPath(pdfPath)}\"";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            SetForegroundWindow(dialog);
            SendMessage(edit, WmSetText, IntPtr.Zero, quotedPath);

            // The Explorer-style picker updates its default button
            // asynchronously after WM_SETTEXT. Dispatch IDOK to the dialog
            // itself and retain BM_CLICK as a fallback; retrying makes the
            // automation resilient when the shell ignores the first command.
            Thread.Sleep(250);
            SendMessage(dialog, WmCommand, new IntPtr(IdOk), openButton);
            Thread.Sleep(250);

            // The shell can retain the HWND briefly after accepting the file,
            // so visibility is a more accurate completion signal than IsWindow.
            if (!IsWindow(dialog) || !IsWindowVisible(dialog))
            {
                return;
            }

            SendMessage(openButton, BmClick, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(250);
            if (!IsWindow(dialog) || !IsWindowVisible(dialog))
            {
                return;
            }
        }

        throw new TimeoutException("Timed out waiting for the native PDF picker to close.");
    }

    public static void DragWithin(Rectangle bounds, int processId)
    {
        if (bounds.Width < 80 || bounds.Height < 80)
        {
            throw new InvalidOperationException($"PDF preview has an unusable UIA rectangle: {bounds}.");
        }

        var mainWindow = WaitForWindow(
            className: null,
            windowName: "Vimes SignSDK Showcase Studio",
            processId,
            TimeSpan.FromSeconds(5));

        SetForegroundWindow(mainWindow);

        var start = new Point(
            bounds.Left + (int)(bounds.Width * 0.25),
            bounds.Top + (int)(bounds.Height * 0.30));
        var end = new Point(
            bounds.Left + (int)(bounds.Width * 0.55),
            bounds.Top + (int)(bounds.Height * 0.45));

        SetCursorPos(start.X, start.Y);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        try
        {
            const int steps = 10;
            for (var step = 1; step <= steps; step++)
            {
                var x = start.X + ((end.X - start.X) * step / steps);
                var y = start.Y + ((end.Y - start.Y) * step / steps);
                SetCursorPos(x, y);
                Thread.Sleep(30);
            }
        }
        finally
        {
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    private static IntPtr WaitForWindow(
        string? className,
        string windowName,
        int processId,
        TimeSpan timeout)
    {
        IntPtr handle = IntPtr.Zero;
        WaitUntil(
            () =>
            {
                handle = FindWindow(className, windowName);
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                GetWindowThreadProcessId(handle, out var ownerProcessId);
                return ownerProcessId == (uint)processId;
            },
            timeout,
            $"window '{windowName}' owned by process {processId}");

        return handle;
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static void EnsureHandle(IntPtr handle, string description)
    {
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"The native Open dialog did not expose its {description}.");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        [MarshalAs(UnmanagedType.LPWStr)] string lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        IntPtr hWnd,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint dwFlags,
        uint dx,
        uint dy,
        uint dwData,
        UIntPtr dwExtraInfo);
}
