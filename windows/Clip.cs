// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
namespace BoardSwitcher;

/// <summary>Буфер обмена с повторами: другое приложение может ненадолго держать его открытым.</summary>
internal static class Clip
{
    private static T? Retry<T>(Func<T> action)
    {
        for (int i = 0; i < 10; i++)
        {
            try { return action(); }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(20); }
        }
        return default;
    }

    public static string? GetText() =>
        Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);

    /// <summary>Положить служебный текст. Он помечается, чтобы журнал буфера обмена Windows (Win+V) его не сохранял.</summary>
    public static void SetServiceText(string text) => Retry(() =>
    {
        var data = new DataObject();
        data.SetText(text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        Clipboard.SetDataObject(data, true);
        return true;
    });

    /// <summary>Вернуть прежний текст (или очистить буфер, если текста не было).</summary>
    public static void Restore(string? old) => Retry(() =>
    {
        if (old is null)
            Clipboard.Clear();
        else
            Clipboard.SetText(old);
        return true;
    });
}
