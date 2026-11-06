// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using System.Security;
using System.Security.Principal;

namespace BoardSwitcher;

/// <summary>
/// Задача Планировщика заданий для текущего пользователя.
/// Нужна для автозапуска и для запуска с правами администратора без окна UAC.
/// Задача есть, если включён автозапуск или права администратора; триггер «при входе» включён только при автозапуске.
///
/// Работа идёт через COM-интерфейс Планировщика (Schedule.Service): описание задачи передаётся строкой,
/// без временных файлов и без запуска schtasks.exe (их можно было бы подменить).
/// </summary>
internal static class Tasks
{
    private const string Prefix = "boardswitcher-";
    private const int TASK_CREATE_OR_UPDATE = 6;
    private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
    private const int TASK_ENUM_HIDDEN = 1;

    public static string Name => Prefix + Environment.UserName;

    private static dynamic RootFolder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    public static bool Exists()
    {
        try
        {
            RootFolder().GetTask(Name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Start()
    {
        try
        {
            RootFolder().GetTask(Name).Run(null);
            return true;
        }
        catch (Exception e)
        {
            Log.Write("не удалось запустить задачу Планировщика: " + e.Message);
            return false;
        }
    }

    /// <summary>Привести задачу в нужное состояние. Для прав администратора нужен процесс с этими правами.</summary>
    public static bool Apply(bool autostart, bool admin)
    {
        if (admin && !Elevation.IsProtectedLocation)
        {
            Log.Write("отказ: запуск с правами администратора возможен только для программы, установленной в Program Files");
            return false;
        }

        if (!autostart && !admin)
            return !Exists() || Delete(Name);

        string exe = Environment.ProcessPath!;
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>boardswitcher: Caps Lock меняет раскладку слова или выделенного текста</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>{(autostart ? "true" : "false")}</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{(admin ? "HighestAvailable" : "LeastPrivilege")}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        try
        {
            RootFolder().RegisterTask(Name, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
            Log.Write($"задача Планировщика обновлена: автозапуск {(autostart ? "вкл" : "выкл")}, " +
                      $"права {(admin ? "администратора" : "обычные")}");
            return true;
        }
        catch (Exception e)
        {
            Log.Write("задача Планировщика НЕ обновлена: " + e.Message);
            return false;
        }
    }

    /// <summary>Удалить задачи boardswitcher всех пользователей (при удалении программы).</summary>
    public static void DeleteAll()
    {
        var names = new List<string>();
        try
        {
            foreach (dynamic task in RootFolder().GetTasks(TASK_ENUM_HIDDEN))
            {
                string name = task.Name;
                if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }
        catch (Exception e)
        {
            Log.Write("не удалось получить список задач Планировщика: " + e.Message);
            return;
        }
        foreach (string name in names)
            Delete(name);
    }

    private static bool Delete(string name)
    {
        try
        {
            RootFolder().DeleteTask(name, 0);
            Log.Write($"задача Планировщика {name} удалена");
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"задача Планировщика {name} НЕ удалена: {e.Message}");
            return false;
        }
    }
}
