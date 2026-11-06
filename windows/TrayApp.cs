// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BoardSwitcher;

/// <summary>Значок в трее: «Настройки…», «Включено», «Выход». Двойной щелчок открывает настройки.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Switcher _switcher;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly MessageWindow _messages;
    private KeyboardHook? _hook;
    private SettingsForm? _settingsForm;
    private IntPtr _iconHandle;

    public TrayApp(SynchronizationContext ui, string? warning)
    {
        _switcher = new Switcher(ui, Layouts.Build()) { Enabled = Settings.Enabled };
        Log.Write($"boardswitcher {Application.ProductVersion} запущен " +
                  $"({(Elevation.IsElevated ? "с правами администратора" : "с обычными правами")}). " +
                  _switcher.Layouts.Describe());

        _enabledItem = new ToolStripMenuItem("Включено") { Checked = _switcher.Enabled, CheckOnClick = true };
        _enabledItem.CheckedChanged += (_, _) => SwitcherEnabled = _enabledItem.Checked;

        var settingsItem = new ToolStripMenuItem("Настройки…", null, (_, _) => ShowSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);

        var menu = new ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Quit());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        UpdateIcon();

        InstallHook();

        // Раскладки могли добавить или убрать: тогда перестраиваем таблицы.
        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => CheckLayouts();
        _timer.Start();

        _messages = new MessageWindow(ShowSettings);

        if (warning is not null)
            _tray.ShowBalloonTip(8000, "boardswitcher", warning, ToolTipIcon.Warning);
        else if (!_switcher.Layouts.Valid)
            WarnLayouts();

        _ = RefreshStartupAsync();
    }

    // ---------- настройки ----------

    public bool SwitcherEnabled
    {
        get => _switcher.Enabled;
        set
        {
            if (_switcher.Enabled == value)
                return;
            Log.Write(value ? "программа включена" : "программа выключена");
            _switcher.Enabled = value;
            Settings.Enabled = value;
            _enabledItem.Checked = value;
            UpdateIcon();
        }
    }

    public void SetLog(bool value)
    {
        if (Log.Enabled == value)
            return;
        if (value)
        {
            Log.Enabled = true;
            Log.Write($"журнал включён. {_switcher.Layouts.Describe()}");
        }
        else
        {
            Log.Write("журнал выключен");
            Log.Enabled = false;
        }
        Settings.Log = value;
    }

    /// <summary>Изменить автозапуск и права администратора (задача Планировщика).</summary>
    public async Task<bool> ApplyStartupAsync(bool autostart, bool admin)
    {
        if (admin && !Elevation.IsProtectedLocation)
            return false;
        // Создать или убрать задачу с правами администратора может только процесс с этими правами.
        bool needAdmin = admin || Settings.RunAsAdmin;
        bool ok;
        if (needAdmin && !Elevation.IsElevated)
        {
            ok = await Task.Run(() => Elevation.RunElevatedHelper($"--apply-task {(autostart ? 1 : 0)} {(admin ? 1 : 0)}"));
        }
        else
        {
            ok = await Task.Run(() => Tasks.Apply(autostart, admin));
            if (ok)
            {
                Settings.Autostart = autostart;
                Settings.RunAsAdmin = admin;
            }
        }
        return ok;
    }

    /// <summary>Перезапуститься с правами администратора или без них.</summary>
    public async Task RestartAsync(bool elevated)
    {
        Log.Write(elevated ? "перезапуск с правами администратора" : "перезапуск с обычными правами");
        _hook?.Dispose();
        _hook = null;
        Program.ReleaseMutex();

        bool ok = await Task.Run(() => elevated ? Elevation.StartElevated() : Elevation.StartNormal());
        if (ok)
        {
            Quit();
            return;
        }

        // Не получилось: продолжаем работать как были.
        Program.AcquireMutex();
        InstallHook();
        MessageBox.Show("Не удалось перезапустить программу. Она продолжает работать " +
                        (Elevation.IsElevated ? "с правами администратора." : "с обычными правами."),
                        "boardswitcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>При запуске: перенести старый автозапуск (Run) и обновить путь к программе в задаче.</summary>
    private async Task RefreshStartupAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                if (Settings.LegacyRunExists())
                {
                    Settings.RemoveLegacyRun();
                    Settings.Autostart = true;
                    Log.Write("старый автозапуск через реестр заменён задачей Планировщика");
                }
                bool autostart = Settings.Autostart, admin = Settings.RunAsAdmin;
                if ((autostart || admin) && (!admin || Elevation.IsElevated))
                    Tasks.Apply(autostart, admin);
            });
        }
        catch (Exception e)
        {
            Log.Write("ОШИБКА при настройке автозапуска: " + e.Message);
        }
    }

    public void ShowSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
            _settingsForm = new SettingsForm(this);
        _settingsForm.Show();
        if (_settingsForm.WindowState == FormWindowState.Minimized)
            _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    public void OpenLog()
    {
        if (!File.Exists(Log.FilePath))
        {
            MessageBox.Show(Log.Enabled ? "Журнал пока пуст: нажмите Caps Lock." : "Журнал пуст. Включите «Вести журнал».",
                "boardswitcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // С правами администратора открываем через Проводник: файл откроется программой, назначенной
        // для .log, но с правами пользователя. Иначе подмена этого назначения дала бы чужой программе права администратора.
        var psi = Elevation.IsElevated
            ? new System.Diagnostics.ProcessStartInfo(Elevation.ExplorerPath, $"\"{Log.FilePath}\"") { UseShellExecute = false }
            : new System.Diagnostics.ProcessStartInfo(Log.FilePath) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }

    // ---------- служебное ----------

    private void InstallHook()
    {
        _hook = new KeyboardHook
        {
            KeyDown = _switcher.OnKeyDown,
            KeyUp = _switcher.OnKeyUp,
            MouseDown = _switcher.OnMouseDown,
        };
    }

    private void CheckLayouts()
    {
        var list = Layouts.SystemList();
        if (_switcher.Layouts.Matches(list))
            return;
        bool wasValid = _switcher.Layouts.Valid;
        _switcher.Layouts = Layouts.Build();
        Log.Write($"раскладки изменились, таблицы перестроены. {_switcher.Layouts.Describe()}");
        if (wasValid && !_switcher.Layouts.Valid)
            WarnLayouts();
        UpdateIcon();
    }

    private void WarnLayouts() =>
        _tray.ShowBalloonTip(5000, "boardswitcher",
            "Нужны две раскладки клавиатуры (например, английская и русская). Пока их меньше, Caps Lock работает как обычно.",
            ToolTipIcon.Warning);

    private void UpdateIcon()
    {
        bool on = _switcher.Enabled && _switcher.Layouts.Valid;
        var old = _iconHandle;
        using (var bmp = DrawIcon(on))
        {
            _iconHandle = bmp.GetHicon();
            _tray.Icon = Icon.FromHandle(_iconHandle);
        }
        if (old != IntPtr.Zero)
            Native.DestroyIcon(old);
        string text = "boardswitcher: " + (on ? "включён (Caps Lock)" : "выключен");
        if (Elevation.IsElevated)
            text += ", администратор";
        _tray.Text = text;
    }

    private static Bitmap DrawIcon(bool on)
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using (var path = new GraphicsPath())
        {
            const int r = 8;
            var rc = new Rectangle(1, 1, 30, 30);
            path.AddArc(rc.X, rc.Y, r, r, 180, 90);
            path.AddArc(rc.Right - r, rc.Y, r, r, 270, 90);
            path.AddArc(rc.Right - r, rc.Bottom - r, r, r, 0, 90);
            path.AddArc(rc.X, rc.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(on ? Color.FromArgb(0x2B, 0x6C, 0xD4) : Color.FromArgb(0x80, 0x80, 0x80));
            g.FillPath(brush, path);
        }

        using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("Ая", font, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        return bmp;
    }

    private void Quit()
    {
        Log.Write("выход");
        _timer.Stop();
        _hook?.Dispose();
        _hook = null;
        _settingsForm?.Close();
        _messages.DestroyHandle();
        _tray.Visible = false;
        _tray.Dispose();
        if (_iconHandle != IntPtr.Zero)
            Native.DestroyIcon(_iconHandle);
        ExitThread();
    }

    /// <summary>Скрытое окно: принимает от повторно запущенной копии просьбу открыть настройки.</summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action _onShowSettings;

        public MessageWindow(Action onShowSettings)
        {
            _onShowSettings = onShowSettings;
            CreateHandle(new CreateParams { Caption = "boardswitcher" });
            // Копия без прав администратора тоже должна достучаться до копии с правами.
            Native.ChangeWindowMessageFilterEx(Handle, (uint)Program.ShowSettingsMessage, Native.MSGFLT_ALLOW, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowSettingsMessage)
                _onShowSettings();
            else
                base.WndProc(ref m);
        }
    }
}
