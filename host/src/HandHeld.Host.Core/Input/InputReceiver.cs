using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace HandHeld.Host.Core.Input;

/// <summary>Receives gamepad/keyboard/mouse UDP packets and injects them into Windows.</summary>
public sealed class InputReceiver : IDisposable
{
    public const int Port = 45350;

    private readonly UdpClient _udp = new(Port);
    private CancellationTokenSource? _cts;
    private Task? _task;

    // Xbox 360 virtual pad (ViGEm) — null if the driver isn't installed.
    private readonly Nefarius.ViGEm.Client.ViGEmClient? _vigem;
    private readonly Nefarius.ViGEm.Client.Targets.IXbox360Controller? _pad;
    private readonly object _padLock = new();

    public bool GamepadAvailable => _pad != null;

    public InputReceiver()
    {
        try
        {
            _vigem = new Nefarius.ViGEm.Client.ViGEmClient();
            _pad = _vigem.CreateXbox360Controller();
        }
        catch
        {
            _pad = null;
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => Loop(_cts.Token));
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = _udp.ReceiveAsync(ct).GetAwaiter().GetResult();
                var data = result.Buffer;
                if (data.Length < 4) continue;
                var type = data[0];
                switch (type)
                {
                    case 0: HandleGamepad(data); break;
                    case 1: HandleKeyboard(data); break;
                    case 2: HandleMouse(data); break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // drop bad packet
            }
        }
    }

    // ---- Gamepad (Type 0) ------------------------------------------------
    private void HandleGamepad(byte[] data)
    {
        if (_pad == null || data.Length < 40) return;

        ushort buttons = (ushort)((data[1] << 8) | data[2]);
        short lx = (short)((data[3] << 8) | data[4]);
        short ly = (short)((data[5] << 8) | data[6]);
        short rx = (short)((data[7] << 8) | data[8]);
        short ry = (short)((data[9] << 8) | data[10]);
        byte lt = data[11];
        byte rt = data[12];

        lock (_padLock)
        {
            _pad.SetButtonsFull(buttons);
            _pad.SetAxisValue(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.LeftThumbX, lx);
            _pad.SetAxisValue(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.LeftThumbY, ly);
            _pad.SetAxisValue(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.RightThumbX, rx);
            _pad.SetAxisValue(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.RightThumbY, ry);
            _pad.SetSliderValue(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Slider.LeftTrigger, lt);
            _pad.SetSliderValue(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Slider.RightTrigger, rt);
        }
    }

    // ---- Keyboard (Type 1) ----------------------------------------------
    private void HandleKeyboard(byte[] data)
    {
        // data: type(1), device(1), size(1) | modifiers(1), key(1), down(1)
        if (data.Length < 7) return;
        byte vk = data[4];
        bool down = data[5] != 0;
        KeybdEvent(vk, down);
    }

    // ---- Mouse (Type 2) --------------------------------------------------
    private void HandleMouse(byte[] data)
    {
        // data: type(2), device(1), size(1) | buttons(1), dx(int16), dy(int16), wheel(int8)
        if (data.Length < 10) return;
        byte buttons = data[3];
        short dx = (short)((data[4] << 8) | data[5]);
        short dy = (short)((data[6] << 8) | data[7]);
        sbyte wheel = (sbyte)data[8];

        // Relative move.
        if (dx != 0 || dy != 0)
        {
            MouseEvent(dx, dy, MOUSEEVENTF_MOVE);
        }
        if (wheel != 0)
        {
            MouseEvent(0, 0, MOUSEEVENTF_WHEEL, (uint)wheel);
        }

        // Button state changes (left=1, right=2, middle=4).
        const int L = 0x01, R = 0x02, M = 0x04;
        MouseButton(L, (buttons & L) != 0);
        MouseButton(R, (buttons & R) != 0);
        MouseButton(M, (buttons & M) != 0);
    }

    private void MouseButton(int flag, bool down)
    {
        uint evt = down
            ? (uint)(flag switch { 1 => 0x0002, 2 => 0x0008, _ => 0x0020 })
            : (uint)(flag switch { 1 => 0x0004, 2 => 0x0010, _ => 0x0040 });
        MouseEvent(0, 0, evt);
    }

    // ---- Win32 injection -------------------------------------------------
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private void KeybdEvent(byte vk, bool down)
    {
        keybd_event(vk, 0, down ? 0 : KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void MouseEvent(int dx, int dy, uint flags, uint data = 0)
    {
        mouse_event(flags, dx, dy, data, UIntPtr.Zero);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp.Close();
        _pad?.Disconnect();
        _vigem?.Dispose();
    }
}
