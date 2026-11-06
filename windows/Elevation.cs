// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace BoardSwitcher;

/// <summary>Права администратора: проверка и перезапуск программы с нужными правами.</summary>
internal static class Elevation
{
    private static string Exe => Environment.ProcessPath!;

    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>
    /// Программа установлена в Program Files, куда без прав администратора писать нельзя.
    /// Только тогда разрешён запуск с правами администратора: иначе любая программа с правами
    /// пользователя могла бы подменить exe (или подложить рядом библиотеку) и получить эти права.
    /// </summary>
    public static bool IsProtectedLocation { get; } = CheckProtectedLocation();

    public static string ExplorerPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static bool CheckProtectedLocation()
    {
        try
        {
            string exe = Path.GetFullPath(Environment.ProcessPath!);
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(root))
                    continue;
                root = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                if (exe.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>Выполнить команду программы с правами администратора (окно UAC) и дождаться результата.</summary>
    public static bool RunElevatedHelper(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Exe, args) { UseShellExecute = true, Verb = "runas" })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception e)
        {
            Log.Write("права администратора не получены: " + e.Message);
            return false;
        }
    }

    /// <summary>Запустить новую копию с правами администратора: через задачу Планировщика (без UAC) или с окном UAC.</summary>
    public static bool StartElevated()
    {
        if (!IsProtectedLocation)
        {
            Log.Write("отказ: запуск с правами администратора возможен только для программы, установленной в Program Files");
            return false;
        }
        if (Settings.RunAsAdmin && Tasks.Exists() && Tasks.Start())
        {
            Log.Write("запуск с правами администратора через задачу Планировщика");
            return true;
        }
        try
        {
            Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, Verb = "runas" });
            Log.Write("запуск с правами администратора через UAC");
            return true;
        }
        catch (Win32Exception e)
        {
            Log.Write("запуск с правами администратора не удался: " + e.Message);
            return false;
        }
    }

    /// <summary>Запустить новую копию с обычными правами (из процесса с правами администратора).</summary>
    public static bool StartNormal()
    {
        if (!Settings.RunAsAdmin && Tasks.Exists() && Tasks.Start())
        {
            Log.Write("запуск с обычными правами через задачу Планировщика");
            return true;
        }
        try
        {
            // Проводник запускает программы с правами пользователя, а не администратора.
            Process.Start(new ProcessStartInfo(ExplorerPath, $"\"{Exe}\"") { UseShellExecute = false });
            Log.Write("запуск с обычными правами через Проводник");
            return true;
        }
        catch (Exception e)
        {
            Log.Write("запуск с обычными правами не удался: " + e.Message);
            return false;
        }
    }
}
