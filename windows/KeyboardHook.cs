// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BoardSwitcher;

/// <summary>
/// Глобальные низкоуровневые хуки клавиатуры и мыши (аналог X11 RECORD в версии для Linux).
/// Нажатия, сымитированные программами (в том числе нами самими), не передаются дальше.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Native.HookProc _kbProc;
    private readonly Native.HookProc _msProc;
    private IntPtr _kbHook;
    private IntPtr _msHook;

    /// <summary>Нажатие клавиши. Вернуть true, чтобы «съесть» его.</summary>
    public Func<int, bool>? KeyDown;

    /// <summary>Отпускание клавиши. Вернуть true, чтобы «съесть» его.</summary>
    public Func<int, bool>? KeyUp;

    /// <summary>Нажатие левой, средней или правой кнопки мыши.</summary>
    public Action? MouseDown;

    public KeyboardHook()
    {
        _kbProc = KeyboardCallback;
        _msProc = MouseCallback;
        IntPtr hMod = Native.GetModuleHandle(null);

        _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, hMod, 0);
        if (_kbHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить перехват клавиатуры");

        _msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, hMod, 0);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & Native.LLKHF_INJECTED) == 0)
            {
                int msg = (int)wParam;
                try
                {
                    bool eat = msg switch
                    {
                        Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN => KeyDown?.Invoke((int)k.vkCode) == true,
                        Native.WM_KEYUP or Native.WM_SYSKEYUP => KeyUp?.Invoke((int)k.vkCode) == true,
                        _ => false,
                    };
                    if (eat)
                        return 1;
                }
                catch
                {
                    // Ошибка в обработчике не должна ломать ввод во всей системе.
                }
            }
        }
        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
            {
                try { MouseDown?.Invoke(); } catch { }
            }
        }
        return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_msHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_msHook); _msHook = IntPtr.Zero; }
    }
}
