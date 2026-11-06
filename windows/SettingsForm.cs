// Copyright (c) 2026 Dmitrii Markov
// SPDX-License-Identifier: MIT
namespace BoardSwitcher;

/// <summary>Окно настроек.</summary>
internal sealed class SettingsForm : Form
{
    private const int TextWidth = 440;

    private readonly TrayApp _app;
    private readonly CheckBox _enabled;
    private readonly CheckBox _autostart;
    private readonly CheckBox _admin;
    private readonly CheckBox _log;
    private readonly Button _ok;
    private readonly Button _cancel;

    public SettingsForm(TrayApp app)
    {
        _app = app;

        Text = "boardswitcher — настройки";
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 12, 14, 10),
        };

        _enabled = Check("Включено: Caps Lock меняет раскладку слова или выделенного текста", app.SwitcherEnabled);
        _autostart = Check("Запускать при входе в Windows", Settings.Autostart);
        _admin = Check("Запускать с правами администратора", Settings.RunAsAdmin);
        _log = Check("Вести журнал (отладка)", Log.Enabled);

        var openLog = new LinkLabel { Text = "Открыть журнал", AutoSize = true, Margin = new Padding(20, 2, 3, 6) };
        openLog.LinkClicked += (_, _) => _app.OpenLog();

        layout.Controls.Add(_enabled);
        layout.Controls.Add(Group("Запуск"));
        layout.Controls.Add(_autostart);
        layout.Controls.Add(_admin);
        layout.Controls.Add(Hint(
            "Нужно, чтобы программа работала и в окнах, открытых от имени администратора. " +
            "Windows один раз спросит разрешение, программа перезапустится."));
        if (!Elevation.IsProtectedLocation)
        {
            _admin.Enabled = false;
            layout.Controls.Add(Hint(
                "Доступно только для установленной программы (в C:\\Program Files): из другой папки " +
                "любая программа могла бы подменить файл и получить права администратора."));
        }
        layout.Controls.Add(Hint("Сейчас программа работает " +
            (Elevation.IsElevated ? "с правами администратора." : "с обычными правами.")));
        layout.Controls.Add(Group("Отладка"));
        layout.Controls.Add(_log);
        layout.Controls.Add(Hint(
            $"Файл: {Log.FilePath}. В журнал записываются набранные слова и выделенный текст: " +
            "не включайте его при работе с паролями."));
        layout.Controls.Add(openLog);

        _ok = new Button { Text = "ОК", AutoSize = true, MinimumSize = new Size(88, 0) };
        _cancel = new Button { Text = "Отмена", AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel };
        _ok.Click += async (_, _) => await ApplyAsync();
        _cancel.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
        };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_ok);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    private static CheckBox Check(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 2),
    };

    private static Label Group(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
        Margin = new Padding(3, 14, 3, 2),
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(TextWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(20, 0, 3, 4),
    };

    private async Task ApplyAsync()
    {
        _ok.Enabled = _cancel.Enabled = false;
        try
        {
            _app.SwitcherEnabled = _enabled.Checked;
            _app.SetLog(_log.Checked);

            bool oldAdmin = Settings.RunAsAdmin;
            bool autostart = _autostart.Checked, admin = _admin.Checked;
            if (autostart != Settings.Autostart || admin != oldAdmin)
            {
                if (!await _app.ApplyStartupAsync(autostart, admin))
                {
                    MessageBox.Show(this,
                        "Не удалось изменить параметры запуска. Если Windows спрашивала разрешение, нужно ответить «Да».",
                        "boardswitcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _autostart.Checked = Settings.Autostart;
                    _admin.Checked = Settings.RunAsAdmin;
                    return;
                }
            }

            Close();
            if (admin != oldAdmin && admin != Elevation.IsElevated)
                await _app.RestartAsync(admin);
        }
        finally
        {
            if (!IsDisposed)
                _ok.Enabled = _cancel.Enabled = true;
        }
    }
}
