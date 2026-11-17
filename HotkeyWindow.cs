namespace ScreenCapture;

/// <summary>Message-only window that receives WM_HOTKEY.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    public event Action<int>? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "ScreenCaptureHotkeySink", Parent = new IntPtr(-3) });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
