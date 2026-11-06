// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BoardSwitcher;

/// <summary>
/// Журнал для проверки работы (аналог --debug в версии для Linux).
/// Внимание: в журнал пишется набранное слово и выделенный текст.
///
/// Файл лежит в профиле пользователя, а писать в него может процесс с правами администратора.
/// Поэтому пишем только в обычный файл: символьные ссылки, точки повторной обработки и жёсткие ссылки
/// отвергаются (иначе программа с правами пользователя могла бы подсунуть ссылку на системный файл).
/// Проверка делается по открытому дескриптору, так что подменить файл между проверкой и записью нельзя.
/// </summary>
internal static class Log
{
    private const long MaxSize = 1024 * 1024;
    private static readonly object Sync = new();

    public static bool Enabled { get; set; }

    /// <summary>Журнал лежит в корне профиля пользователя: C:\Users\&lt;имя&gt;\boardswitcher.log.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "boardswitcher.log");

    public static void Write(string message)
    {
        if (!Enabled)
            return;
        try
        {
            lock (Sync)
            {
                using var handle = OpenSafe();
                if (handle is null)
                    return;
                using var fs = new FileStream(handle, FileAccess.ReadWrite);
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
                if (fs.Length > MaxSize)
                {
                    fs.SetLength(0);
                    line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  журнал превысил 1 МБ и очищен{Environment.NewLine}" + line;
                }
                fs.Seek(0, SeekOrigin.End);
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                fs.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            // журнал не должен мешать работе
        }
    }

    /// <summary>Открыть (или создать) файл журнала, если это обычный файл без ссылок.</summary>
    private static SafeFileHandle? OpenSafe()
    {
        var h = CreateFile(FilePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ, IntPtr.Zero,
            OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h.Dispose();
            return null;
        }
        if (!GetFileInformationByHandle(h, out var info) ||
            (info.FileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) != 0 ||
            info.NumberOfLinks != 1)
        {
            h.Dispose();
            return null;
        }
        return h;
    }

    /// <summary>Строка для журнала: в кавычках, с видимыми переводами строк.</summary>
    public static string Quote(string s) =>
        "«" + s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "»";

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint OPEN_ALWAYS = 4;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);
}
