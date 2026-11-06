// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace BoardSwitcher;

/// <summary>Имитация нажатий (аналог XTest в версии для Linux).</summary>
internal static class Input
{
    private static readonly int Size = Marshal.SizeOf<Native.INPUT>();

    private static bool IsExtended(int vk) => vk is
        Native.VK_INSERT or Native.VK_DELETE or Native.VK_HOME or Native.VK_END or
        Native.VK_PRIOR or Native.VK_NEXT or Native.VK_LEFT or Native.VK_UP or
        Native.VK_RIGHT or Native.VK_DOWN or Native.VK_RCONTROL or Native.VK_RMENU or
        Native.VK_LWIN or Native.VK_RWIN;

    private static Native.INPUT Key(int vk, bool up)
    {
        uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
        if (IsExtended(vk))
            flags |= Native.KEYEVENTF_EXTENDEDKEY;
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)Native.MapVirtualKeyEx((uint)vk, 0, IntPtr.Zero),
                    dwFlags = flags,
                },
            },
        };
    }

    private static Native.INPUT Unicode(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
            },
        },
    };

    private static void Send(List<Native.INPUT> inputs)
    {
        if (inputs.Count > 0)
            Native.SendInput((uint)inputs.Count, inputs.ToArray(), Size);
    }

    public static void Tap(int vk) => Send(new() { Key(vk, false), Key(vk, true) });

    public static void Combo(int mod, int vk) =>
        Send(new() { Key(mod, false), Key(vk, false), Key(vk, true), Key(mod, true) });

    public static void Backspaces(int count)
    {
        var list = new List<Native.INPUT>(count * 2);
        for (int i = 0; i < count; i++)
        {
            list.Add(Key(Native.VK_BACK, false));
            list.Add(Key(Native.VK_BACK, true));
        }
        Send(list);
    }

    /// <summary>Набрать текст символами Unicode: не зависит от раскладки и не трогает буфер обмена.</summary>
    public static void Type(string text)
    {
        var list = new List<Native.INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            list.Add(Unicode(c, false));
            list.Add(Unicode(c, true));
        }
        Send(list);
    }

    /// <summary>Программно отпустить зажатые Shift, Ctrl, Alt и Win, чтобы они не смешались с нашими нажатиями.</summary>
    public static void ReleaseHeldModifiers()
    {
        var list = new List<Native.INPUT>();
        bool alt = Native.IsDown(Native.VK_LMENU) || Native.IsDown(Native.VK_RMENU);
        bool win = Native.IsDown(Native.VK_LWIN) || Native.IsDown(Native.VK_RWIN);
        if (alt || win)
        {
            // Без этого отпускание Alt открывает меню окна, а Win — меню «Пуск».
            list.Add(Key(Native.VK_UNASSIGNED, false));
            list.Add(Key(Native.VK_UNASSIGNED, true));
        }
        foreach (int vk in new[] { Native.VK_LSHIFT, Native.VK_RSHIFT, Native.VK_LCONTROL, Native.VK_RCONTROL,
                                   Native.VK_LMENU, Native.VK_RMENU, Native.VK_LWIN, Native.VK_RWIN })
        {
            if (Native.IsDown(vk))
                list.Add(Key(vk, true));
        }
        Send(list);
    }

    /// <summary>Снять режим заглавных, если он включён.</summary>
    public static void UnlockCaps()
    {
        if (Native.IsToggled(Native.VK_CAPITAL))
            Tap(Native.VK_CAPITAL);
    }
}
