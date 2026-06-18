using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using MouseButtons = System.Windows.Forms.MouseButtons;
using WinScreen = System.Windows.Forms.Screen;

namespace StickyNotes;

public partial class App : System.Windows.Application
{
    // ==================== 持久化 ====================
    private static readonly string SaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StickyNotes");
    private static readonly string SavePath = Path.Combine(SaveDir, "notes.json");

    private DispatcherTimer? _saveTimer;

    // ==================== 状态 ====================
    private NotifyIcon? _trayIcon;
    private HelpWindow? _helpWindow;
    private readonly List<StickyNoteWindow> _notes = new();
    private int _nextOffset;

    // ==================== 启动 ====================

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CreateTrayIcon();
        RestoreNotes();
    }

    // ==================== 托盘图标 ====================

    private void CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("新建便签", null, (_, _) => CreateNote()));
        menu.Items.Add(new ToolStripMenuItem("使用说明", null, (_, _) => ShowHelp()));
        menu.Items.Add(new ToolStripSeparator());

        // 开机自启（可打钩）
        var autoStartItem = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled()
        };
        autoStartItem.Click += (_, _) =>
        {
            if (autoStartItem.Checked)
                EnableAutoStart();
            else
                DisableAutoStart();
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApp()));

        _trayIcon = new NotifyIcon
        {
            Icon = CreateAppIcon(),
            Text = "桌面便签",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                CreateNote();
        };
    }

    // ==================== 开机自启（注册表） ====================

    private const string AutoRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoRunValueName = "StickyNotes";

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoRunKey, writable: false);
            return key?.GetValue(AutoRunValueName) is string val
                   && val == GetExePath();
        }
        catch
        {
            return false;
        }
    }

    private static void EnableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoRunKey, writable: true);
            key?.SetValue(AutoRunValueName, GetExePath());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"注册表写入失败: {ex.Message}");
        }
    }

    private static void DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoRunKey, writable: true);
            key?.DeleteValue(AutoRunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"注册表删除失败: {ex.Message}");
        }
    }

    private static string GetExePath()
    {
        // ProcessPath 在 .NET 8 单文件打包后仍返回真实 exe 路径
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
    }

    // ==================== 保存队列（防抖 500ms） ====================

    /// <summary>任意便签状态变化后调用，延迟批量保存。</summary>
    private void RequestSave()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SaveAll();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveAll()
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var data = _notes.Select(n => n.ToNoteData()).ToArray();
            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            // 静默失败，避免保存异常影响使用
            System.Diagnostics.Debug.WriteLine($"保存失败: {ex.Message}");
        }
    }

    // ==================== 恢复 ====================

    private void RestoreNotes()
    {
        if (!File.Exists(SavePath))
        {
            // 首次运行：弹一张默认便签
            CreateNote();
            return;
        }

        NoteData[]? notes = null;
        try
        {
            var json = File.ReadAllText(SavePath);
            notes = JsonSerializer.Deserialize<NoteData[]>(json);
        }
        catch (Exception ex)
        {
            // JSON 损坏：备份损坏文件，以空白重置
            System.Diagnostics.Debug.WriteLine($"JSON 解析失败: {ex.Message}");
            try
            {
                string bakPath = SavePath + ".bak";
                File.Move(SavePath, bakPath, overwrite: true);
            }
            catch { /* 备份失败也不影响启动 */ }
        }

        if (notes == null || notes.Length == 0)
        {
            CreateNote();
            return;
        }

        foreach (var data in notes)
        {
            CreateNote(data);
        }
    }

    // ==================== 帮助窗口 ====================

    private void ShowHelp()
    {
        if (_helpWindow == null)
        {
            _helpWindow = new HelpWindow();
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }

        _helpWindow.Show();
        _helpWindow.Activate(); // 如果已打开就激活到最前
    }

    // ==================== 便签管理 ====================

    public void CreateNote(NoteData? data = null)
    {
        var window = new StickyNoteWindow();

        if (data != null)
        {
            // 恢复模式下直接用数据设置位置/颜色
            window.ApplyLayoutData(data);
        }
        else
        {
            // 新建模式：屏幕居中 + 偏移
            var screen = WinScreen.PrimaryScreen;
            if (screen != null)
            {
                double centerX = (screen.WorkingArea.Width - window.Width) / 2;
                double centerY = (screen.WorkingArea.Height - window.Height) / 2;
                window.Left = centerX + _nextOffset;
                window.Top = centerY + _nextOffset;
                _nextOffset = (_nextOffset + 30) % 300;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        // 状态变化 → 防抖保存
        window.NoteStateChanged += RequestSave;

        window.Closed += (_, _) =>
        {
            _notes.Remove(window);
            RequestSave(); // 删除后立即保存
        };

        _notes.Add(window);
        window.Show();
        window.Activate();
    }

    // ==================== 退出 ====================

    private void ExitApp()
    {
        // 退出前做一次最终保存（不依赖防抖）
        SaveAll();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        foreach (var note in _notes.ToList())
        {
            note.Close();
        }
        _notes.Clear();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    // ==================== 图标生成（运行时绘制黄色方框） ====================

    private static Icon CreateAppIcon()
    {
        int size = 32;
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);

        using var fill = new SolidBrush(Color.FromArgb(232, 197, 71));
        g.FillRectangle(fill, 3, 3, 26, 26);

        using var border = new Pen(Color.FromArgb(201, 168, 32), 2);
        g.DrawRectangle(border, 3, 3, 26, 26);

        using var fold = new Pen(Color.FromArgb(180, 150, 20), 1);
        g.DrawLine(fold, 20, 3, 28, 11);
        g.DrawLine(fold, 20, 3, 20, 11);
        g.DrawLine(fold, 20, 11, 28, 11);

        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        Win32.DestroyIcon(hIcon);
        bmp.Dispose();
        return icon;
    }

    private static class Win32
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
