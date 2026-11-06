// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using Microsoft.Win32;

namespace BoardSwitcher;

/// <summary>Настройки пользователя в реестре: HKCU\Software\boardswitcher.</summary>
internal static class Settings
{
    private const string KeyPath = @"Software\boardswitcher";

    // Автозапуск версии 1.1 (запись в Run). Сейчас вместо неё задача Планировщика.
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunName = "boardswitcher";

    /// <summary>Программа включена (Caps Lock меняет раскладку).</summary>
    public static bool Enabled { get => Get("Enabled", true); set => Set("Enabled", value); }

    /// <summary>Запускать при входе в Windows.</summary>
    public static bool Autostart { get => Get("Autostart", false); set => Set("Autostart", value); }

    /// <summary>Запускать с правами администратора.</summary>
    public static bool RunAsAdmin { get => Get("RunAsAdmin", false); set => Set("RunAsAdmin", value); }

    /// <summary>Вести журнал.</summary>
    public static bool Log { get => Get("Log", false); set => Set("Log", value); }

    private static bool Get(string name, bool def)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) is int v ? v != 0 : def;
        }
        catch
        {
            return def;
        }
    }

    private static void Set(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    public static void DeleteAll() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);

    public static bool LegacyRunExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey);
        return key?.GetValue(LegacyRunName) is not null;
    }

    public static void RemoveLegacyRun()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, true);
        key?.DeleteValue(LegacyRunName, false);
    }
}
