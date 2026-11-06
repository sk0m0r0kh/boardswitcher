// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
namespace BoardSwitcher;

internal static class Program
{
    private const string MutexName = @"Local\boardswitcher";
    private static Mutex? _mutex;

    /// <summary>Сообщение «открой настройки»: его шлёт повторно запущенная копия.</summary>
    public static readonly int ShowSettingsMessage = Native.RegisterWindowMessage("boardswitcher-show-settings");

    /// <summary>
    /// Ключи:
    ///   --debug                          журнал на этот запуск;
    ///   --apply-task &lt;автозапуск&gt; &lt;админ&gt;  (служебный) настроить задачу Планировщика, 0/1;
    ///   --uninstall                      (служебный) убрать задачи и настройки при удалении программы.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Log.Enabled = args.Contains("--debug") || Settings.Log;

        if (args.Length >= 3 && args[0] == "--apply-task")
            return ApplyTask(args[1] == "1", args[2] == "1");
        if (args.Contains("--uninstall"))
            return Uninstall();

        ApplicationConfiguration.Initialize();

        if (!AcquireMutex())
        {
            // Уже запущена: просим работающую копию открыть настройки.
            Native.PostMessage(Native.HWND_BROADCAST, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        string? warning = null;
        if (Settings.RunAsAdmin && !Elevation.IsElevated && !Elevation.IsProtectedLocation)
        {
            warning = "Запуск с правами администратора работает только для установленной программы " +
                      "(в C:\\Program Files). Эта копия работает с обычными правами.";
        }
        else if (Settings.RunAsAdmin && !Elevation.IsElevated)
        {
            ReleaseMutex();
            Log.Write("включён запуск с правами администратора, перезапускаюсь с ними");
            if (Elevation.StartElevated())
                return 0;
            if (!AcquireMutex())
                return 0;
            warning = "Не удалось получить права администратора. Программа работает с обычными правами " +
                      "и не будет действовать в окнах, открытых от имени администратора.";
        }

        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        Application.Run(new TrayApp(ui, warning));
        ReleaseMutex();
        return 0;
    }

    public static bool AcquireMutex()
    {
        try
        {
            var m = new Mutex(true, MutexName, out bool created);
            if (!created)
            {
                m.Dispose();
                return false;
            }
            _mutex = m;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // мьютекс создан копией с правами администратора
            return false;
        }
    }

    public static void ReleaseMutex()
    {
        if (_mutex is null)
            return;
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
        _mutex = null;
    }

    private static int ApplyTask(bool autostart, bool admin)
    {
        bool ok = Tasks.Apply(autostart, admin);
        if (ok)
        {
            Settings.Autostart = autostart;
            Settings.RunAsAdmin = admin;
            Settings.RemoveLegacyRun();
        }
        return ok ? 0 : 1;
    }

    private static int Uninstall()
    {
        Tasks.DeleteAll();
        Settings.RemoveLegacyRun();
        Settings.DeleteAll();
        return 0;
    }
}
