// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
namespace BoardSwitcher;

/// <summary>
/// Две первые раскладки системы и таблицы символов для них.
/// Таблицы строятся из самих раскладок Windows (ToUnicodeEx), а не зашиты в код,
/// поэтому учитывают конкретный вариант раскладки.
/// </summary>
internal sealed class Layouts
{
    private readonly IntPtr[] _hkl = new IntPtr[2];
    private readonly Dictionary<(int Vk, bool Level), char>[] _chars = { new(), new() };
    private readonly Dictionary<char, char> _aToB = new();
    private readonly Dictionary<char, char> _bToA = new();

    /// <summary>Раскладок в системе две или больше.</summary>
    public bool Valid { get; }

    /// <summary>Список раскладок, по которому построены таблицы.</summary>
    public IntPtr[] Source { get; }

    private Layouts(IntPtr[] list)
    {
        Source = list;
        if (list.Length < 2)
            return;
        Valid = true;
        _hkl[0] = list[0];
        _hkl[1] = list[1];

        var state = new byte[256];
        var buf = new char[8];
        for (int g = 0; g < 2; g++)
        {
            for (int vk = 0x20; vk <= 0xFE; vk++)
            {
                if (IsModifierVk(vk))
                    continue;
                uint sc = Native.MapVirtualKeyEx((uint)vk, 0 /* MAPVK_VK_TO_VSC */, _hkl[g]);
                if (sc == 0)
                    continue;
                foreach (bool level in new[] { false, true })
                {
                    Array.Clear(state);
                    if (level)
                        state[Native.VK_SHIFT] = 0x80;
                    // Флаг 0x4: не менять состояние «мёртвых» клавиш в системе (Windows 10 1607+).
                    int r = Native.ToUnicodeEx((uint)vk, sc, state, buf, buf.Length, 0x4, _hkl[g]);
                    if (r == 1 && !char.IsControl(buf[0]))
                        _chars[g][(vk, level)] = buf[0];
                }
            }
        }

        foreach (var (key, a) in _chars[0])
        {
            if (_chars[1].TryGetValue(key, out char b))
            {
                _aToB.TryAdd(a, b);
                _bToA.TryAdd(b, a);
            }
        }
    }

    public static Layouts Build() => new(SystemList());

    /// <summary>Описание для журнала: раскладки системы и размер таблиц.</summary>
    public string Describe()
    {
        var names = Source.Select((hkl, i) =>
        {
            int lang = (int)((long)hkl & 0xFFFF);
            string name;
            try { name = System.Globalization.CultureInfo.GetCultureInfo(lang).Name; }
            catch { name = "?"; }
            return $"{i}: {name} (0x{(long)hkl:X8})";
        });
        return $"раскладки [{string.Join(", ", names)}], символов в таблицах: {_chars[0].Count}/{_chars[1].Count}, " +
               $"соответствий: {_aToB.Count}" + (Valid ? "" : " — НУЖНЫ ДВЕ РАСКЛАДКИ");
    }

    public static IntPtr[] SystemList()
    {
        int n = Native.GetKeyboardLayoutList(0, null);
        if (n <= 0)
            return Array.Empty<IntPtr>();
        var list = new IntPtr[n];
        n = Native.GetKeyboardLayoutList(n, list);
        return list.Take(Math.Max(n, 0)).ToArray();
    }

    public bool Matches(IntPtr[] list) => Source.SequenceEqual(list);

    public static bool IsModifierVk(int vk) => vk is
        Native.VK_SHIFT or Native.VK_CONTROL or Native.VK_MENU or Native.VK_CAPITAL or
        Native.VK_LSHIFT or Native.VK_RSHIFT or Native.VK_LCONTROL or Native.VK_RCONTROL or
        Native.VK_LMENU or Native.VK_RMENU or Native.VK_LWIN or Native.VK_RWIN or
        Native.VK_NUMLOCK or Native.VK_SCROLL;

    /// <summary>Символ клавиши в раскладке g (0 или 1) на уровне level (с Shift или без). null — клавиша без символа.</summary>
    public char? CharFor(int vk, int g, bool level) =>
        _chars[g].TryGetValue((vk, level), out char c) ? c : null;

    /// <summary>Номер раскладки активного окна: 0, 1 или -1 (третья и далее, не поддерживается).</summary>
    public int CurrentGroup()
    {
        if (!Valid)
            return -1;
        uint tid = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        IntPtr hkl = Native.GetKeyboardLayout(tid);
        if (hkl == _hkl[0]) return 0;
        if (hkl == _hkl[1]) return 1;
        return -1;
    }

    /// <summary>Поставить раскладку активного окна (0 — первая, 1 — вторая). Работает как Alt+Shift.</summary>
    public void Lock(int g)
    {
        if (!Valid || g is not (0 or 1))
            return;
        if (!Native.PostMessage(Native.GetForegroundWindow(), Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, _hkl[g]))
            Log.Write($"не удалось переключить раскладку на {g} (окно не приняло сообщение)");
    }

    /// <summary>
    /// Перекодировать текст. Меняется раскладка, которой в тексте больше
    /// (по буквам, у которых есть соответствие в другой раскладке).
    /// Возвращает новый текст и раскладку, в которую он превратился.
    /// </summary>
    public (string Text, int Target) ConvertText(string text, int curGroup)
    {
        int na = text.Count(c => char.IsLetter(c) && _aToB.TryGetValue(c, out var r) && r != c);
        int nb = text.Count(c => char.IsLetter(c) && _bToA.TryGetValue(c, out var r) && r != c);
        int src = na == nb ? (curGroup is 0 or 1 ? curGroup : 0) : (na > nb ? 0 : 1);
        var table = src == 0 ? _aToB : _bToA;
        var chars = text.Select(c => table.TryGetValue(c, out var r) ? r : c).ToArray();
        return (new string(chars), 1 - src);
    }
}
