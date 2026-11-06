// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
namespace BoardSwitcher;

/// <summary>
/// Основная логика (перенос класса Punto из версии для Linux).
/// Caps Lock:
///  * если только что набрано слово (после него не было клика мышью, стрелок, Enter и т. п.),
///    меняется раскладка этого слова;
///  * иначе меняется раскладка выделенного текста;
///  * если нет ни слова, ни выделения (или выделены только пробелы и переводы строки),
///    ничего не вставляется, просто переключается раскладка.
/// После конвертации раскладка ставится в ту, в которой теперь текст.
/// Повторное нажатие возвращает слово обратно.
/// </summary>
internal sealed class Switcher
{
    private readonly record struct Key(int Vk, bool Level, int Group);

    private const int MaxWord = 200;

    private readonly SynchronizationContext _ui;
    private Layouts _layouts;
    private readonly List<Key> _word = new();  // текущее слово
    private int _trail;                         // сколько пробелов набрано после слова
    private bool _busy;                         // идёт действие: свои и чужие нажатия не учитываем
    private bool _capsHeld;                     // Caps Lock зажат (защита от автоповтора)
    private bool _enabled = true;

    public Switcher(SynchronizationContext ui, Layouts layouts)
    {
        _ui = ui;
        _layouts = layouts;
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Clear(); }
    }

    public Layouts Layouts
    {
        get => _layouts;
        set { _layouts = value; Clear(); }
    }

    public void Clear()
    {
        _word.Clear();
        _trail = 0;
    }

    public void OnMouseDown()
    {
        if (!_busy)
            Clear();
    }

    public bool OnKeyDown(int vk)
    {
        if (vk == Native.VK_CAPITAL)
        {
            if (!_enabled || !_layouts.Valid)
                return false;           // выключено: Caps Lock работает как обычно
            if (_capsHeld)
                return true;            // автоповтор
            _capsHeld = true;
            if (_busy)
            {
                Log.Write("Caps Lock: пропущено, предыдущее действие ещё выполняется");
                return true;
            }
            _busy = true;
            var word = _word.ToList();
            int trail = _trail;
            int group = _layouts.CurrentGroup();
            Log.Write(word.Count > 0
                ? $"Caps Lock: слово из {word.Count} символов, пробелов после него: {trail}, раскладка окна: {group}"
                : $"Caps Lock: слова нет, проверяю выделение, раскладка окна: {group}");
            // Само действие выполняем после выхода из обработчика перехвата.
            _ui.Post(_ => _ = RunAsync(word, trail, group), null);
            return true;                // «съедаем» нажатие: режим заглавных не включается
        }

        if (_busy || !_enabled || Layouts.IsModifierVk(vk))
            return false;

        int g = _layouts.CurrentGroup();
        if (Native.IsDown(Native.VK_CONTROL) || Native.IsDown(Native.VK_MENU) ||
            Native.IsDown(Native.VK_LWIN) || Native.IsDown(Native.VK_RWIN) || g < 0)
        {
            Clear();
            return false;
        }

        switch (vk)
        {
            case Native.VK_BACK:
                if (_trail > 0)
                    _trail--;
                else if (_word.Count > 0)
                    _word.RemoveAt(_word.Count - 1);
                return false;
            case Native.VK_RETURN:
                Clear();
                return false;
            case Native.VK_SPACE:
                if (_word.Count > 0)
                    _trail++;
                return false;
        }

        bool shift = Native.IsDown(Native.VK_SHIFT);
        char? ch = _layouts.CharFor(vk, g, shift);
        if (ch is null)
        {
            Clear();                    // стрелки, Tab, Esc, F-клавиши и т. п.
            return false;
        }

        bool level = shift;
        if (Native.IsToggled(Native.VK_CAPITAL) && char.IsLetter(ch.Value))
            level = !level;
        if (_trail > 0)
        {
            // после пробела начинается новое слово
            _word.Clear();
            _trail = 0;
        }
        _word.Add(new Key(vk, level, g));
        if (_word.Count > MaxWord)
            Clear();
        return false;
    }

    public bool OnKeyUp(int vk)
    {
        if (vk == Native.VK_CAPITAL && _capsHeld)
        {
            _capsHeld = false;
            return true;
        }
        return false;
    }

    private async Task RunAsync(List<Key> word, int trail, int group)
    {
        try
        {
            Input.UnlockCaps();
            Input.ReleaseHeldModifiers();
            if (word.Count > 0)
                DoWord(word, trail);
            else
                await DoSelectionAsync(group);
        }
        catch (Exception e)
        {
            // не даём программе упасть
            Log.Write("ОШИБКА: " + e);
        }
        finally
        {
            await Task.Delay(100);
            _busy = false;
        }
    }

    private void DoWord(List<Key> word, int trail)
    {
        int src = word[^1].Group;       // в какой раскладке слово набрано (по последней клавише)
        int tgt = 1 - src;
        string neu = string.Concat(word.Select(k =>
                         _layouts.CharFor(k.Vk, tgt, k.Level) ?? _layouts.CharFor(k.Vk, src, k.Level)))
                     + new string(' ', trail);
        if (Log.Enabled)
        {
            string was = string.Concat(word.Select(k => _layouts.CharFor(k.Vk, k.Group, k.Level)));
            Log.Write($"слово: {Log.Quote(was)} -> {Log.Quote(neu.TrimEnd(' '))}, раскладка {src} -> {tgt}");
        }

        Input.Backspaces(word.Count + trail);
        Input.Type(neu);
        _layouts.Lock(tgt);

        // теперь слово «живёт» в другой раскладке: повторное нажатие вернёт его назад
        _word.Clear();
        _word.AddRange(word.Select(k => k with { Group = tgt }));
        _trail = trail;
    }

    private async Task DoSelectionAsync(int group)
    {
        string? old = Clip.GetText();
        string sentinel = "boardswitcher-sentinel-" + DateTime.Now.Ticks;
        Clip.SetServiceText(sentinel);
        Input.Combo(Native.VK_CONTROL, Native.VK_INSERT);   // Ctrl+Insert = копировать

        string text = "";
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(50);
            string? t = Clip.GetText();
            if (!string.IsNullOrEmpty(t) && t != sentinel)
            {
                text = t;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // нет ни слова, ни выделения: просто переключаем раскладку
            Clip.Restore(old);
            int to = 1 - (group is 0 or 1 ? group : 0);
            Log.Write($"выделения нет, просто переключаю раскладку на {to}");
            _layouts.Lock(to);
            return;
        }

        var (neu, tgt) = _layouts.ConvertText(text, group);
        Log.Write($"выделение: {Log.Quote(text)} -> {Log.Quote(neu)}, раскладка -> {tgt}");
        Clip.SetServiceText(neu);
        Input.Combo(Native.VK_SHIFT, Native.VK_INSERT);     // Shift+Insert = вставить
        await Task.Delay(150);
        _layouts.Lock(tgt);
        await Task.Delay(300);
        Clip.Restore(old);
    }
}
