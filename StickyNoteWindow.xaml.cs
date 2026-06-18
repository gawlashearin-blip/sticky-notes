using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;
using Button = System.Windows.Controls.Button;
using DataFormats = System.Windows.DataFormats;

namespace StickyNotes;

public partial class StickyNoteWindow : Window
{
    // ============ 常量 ============
    private const double ResizeBorderThickness = 6.0;

    // ============ 缩放状态 ============
    private bool _isResizing;
    private ResizeDirection _resizeDirection;
    private Point _startScreenPos;
    private double _startLeft, _startTop, _startWidth, _startHeight;

    // ============ 折叠状态 ============
    private bool _isCollapsed;
    private double _expandedHeight;

    // ============ 持久化相关 ============
    private string _noteId = Guid.NewGuid().ToString();
    private string _bgColor = "#E8C547";
    private string _titleBarColor = "#C9A820";
    private bool _isRestoring;                       // 恢复中，抑制保存事件
    private NoteData? _pendingRestore;               // 待恢复的数据（Loaded 时消费）

    /// <summary>任何状态变化时触发，外部订阅做防抖保存。</summary>
    public event Action? NoteStateChanged;

    public string NoteId => _noteId;

    // ============ 构造函数 ============
    public StickyNoteWindow()
    {
        InitializeComponent();
    }

    // ============ 窗口加载 ============
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Ctrl+B/I/U
        ContentBox.InputBindings.Add(new KeyBinding(
            EditingCommands.ToggleBold, Key.B, ModifierKeys.Control));
        ContentBox.InputBindings.Add(new KeyBinding(
            EditingCommands.ToggleItalic, Key.I, ModifierKeys.Control));
        ContentBox.InputBindings.Add(new KeyBinding(
            EditingCommands.ToggleUnderline, Key.U, ModifierKeys.Control));

        // 正文变化 → 通知保存
        ContentBox.TextChanged += (_, _) => NotifyNoteStateChanged();

        // 窗口移动/缩放 → 通知保存
        LocationChanged += (_, _) => NotifyNoteStateChanged();
        SizeChanged += (_, _) => NotifyNoteStateChanged();

        // 消费待恢复数据
        if (_pendingRestore != null)
        {
            _isRestoring = true;
            RestoreContentAndState(_pendingRestore);
            _isRestoring = false;
        }

        ContentBox.Focus();
    }

    // ============ 持久化 ============

    /// <summary>在窗口 Show 之前调用，设置位置/尺寸/颜色。</summary>
    public void ApplyLayoutData(NoteData data)
    {
        _noteId = data.Id;
        _bgColor = data.BgColor;
        _titleBarColor = data.TitleBarColor;
        _expandedHeight = data.Height;

        Left = data.Left;
        Top = data.Top;
        Width = data.Width;

        if (data.IsPinned)
        {
            Topmost = true;
            PinButton.Content = "📍";
            PinButton.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#D4B030"));
            PinButton.ToolTip = "已钉住（置顶）";
        }

        SetColors(_bgColor, _titleBarColor);

        // 收起状态留到 Loaded 后再应用（UI 已就绪）
        _pendingRestore = data;
    }

    /// <summary>在 Loaded 里消费，还原正文内容和折叠状态。</summary>
    private void RestoreContentAndState(NoteData data)
    {
        // 还原富文本
        if (!string.IsNullOrWhiteSpace(data.XamlContent))
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data.XamlContent);
                using var ms = new MemoryStream(bytes);
                var range = new TextRange(
                    ContentBox.Document.ContentStart,
                    ContentBox.Document.ContentEnd);
                range.Load(ms, DataFormats.Xaml);
            }
            catch
            {
                // XAML 解析失败，留空白内容
            }
        }

        // 还原折叠状态
        if (data.IsCollapsed)
        {
            _isCollapsed = true;
            ContentBox.Visibility = Visibility.Collapsed;
            MinHeight = 28;
            Height = 28;
            TitleBarBorder.CornerRadius = new CornerRadius(8);
            CollapseButton.Content = "□";
            CollapseButton.ToolTip = "展开";
            CollapseButton.Padding = new Thickness(0, -1, 0, 0);
        }
    }

    /// <summary>导出当前状态为可序列化对象。</summary>
    public NoteData ToNoteData()
    {
        // 序列化 RichTextBox → XAML 字符串
        var range = new TextRange(
            ContentBox.Document.ContentStart,
            ContentBox.Document.ContentEnd);
        string xaml;
        using (var ms = new MemoryStream())
        {
            range.Save(ms, DataFormats.Xaml);
            xaml = Encoding.UTF8.GetString(ms.ToArray());
        }

        return new NoteData
        {
            Id = _noteId,
            XamlContent = xaml,
            Left = Left,
            Top = Top,
            Width = Width,
            Height = _isCollapsed ? _expandedHeight : Height, // 折叠时存原高
            BgColor = _bgColor,
            TitleBarColor = _titleBarColor,
            IsPinned = Topmost,
            IsCollapsed = _isCollapsed
        };
    }

    private void NotifyNoteStateChanged()
    {
        if (!_isRestoring)
            NoteStateChanged?.Invoke();
    }

    // ==================== 钉住 ====================

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;

        if (Topmost)
        {
            PinButton.Content = "📍";
            PinButton.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#D4B030"));
            PinButton.ToolTip = "已钉住（置顶）";
        }
        else
        {
            PinButton.Content = "📌";
            PinButton.Background = Brushes.Transparent;
            PinButton.ToolTip = "钉住";
        }

        NotifyNoteStateChanged();
    }

    // ==================== 折叠 ====================

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCollapsed)
            Expand();
        else
            Collapse();

        NotifyNoteStateChanged();
    }

    private void Collapse()
    {
        _expandedHeight = Height;
        _isCollapsed = true;

        ContentBox.Visibility = Visibility.Collapsed;
        MinHeight = 28;
        Height = 28;

        TitleBarBorder.CornerRadius = new CornerRadius(8);

        CollapseButton.Content = "□";
        CollapseButton.ToolTip = "展开";
        CollapseButton.Padding = new Thickness(0, -1, 0, 0);
    }

    private void Expand()
    {
        _isCollapsed = false;

        ContentBox.Visibility = Visibility.Visible;
        MinHeight = 120;
        Height = _expandedHeight;

        TitleBarBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);

        CollapseButton.Content = "─";
        CollapseButton.ToolTip = "折叠";
        CollapseButton.Padding = new Thickness(0, -2, 0, 0);
    }

    // ==================== 改颜色 ====================

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = !ColorPopup.IsOpen;
    }

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect || rect.Tag is not string tag)
            return;

        var parts = tag.Split(';');
        if (parts.Length != 2) return;

        _bgColor = parts[0];
        _titleBarColor = parts[1];
        SetColors(_bgColor, _titleBarColor);

        ColorPopup.IsOpen = false;
        NotifyNoteStateChanged();
    }

    /// <summary>应用颜色到 Border 元素。</summary>
    private void SetColors(string bg, string tb)
    {
        var bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        var tbBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tb));
        OuterBorder.Background = bgBrush;
        TitleBarBorder.Background = tbBrush;
        OuterBorder.BorderBrush = tbBrush;
    }

    // ==================== 删除 ====================

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        // 触发 Closed 事件前通知 App 保存（App 会通过 Closed 清理列表）
        NotifyNoteStateChanged();
        Close();
    }

    // ============ 移动（标题条） ============
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    // ============ 鼠标移动：缩放中 or 边缘光标 ============
    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isResizing)
        {
            HandleResizeMove(e);
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            return;

        if (_isCollapsed)
            return;

        var pos = e.GetPosition(this);
        SetResizeCursor(GetResizeDirection(pos));
    }

    // ============ 鼠标按下：开始缩放 ============
    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ColorPopup.IsOpen)
            return;
        if (_isCollapsed)
            return;
        if (e.OriginalSource is Button)
            return;

        var pos = e.GetPosition(this);
        var direction = GetResizeDirection(pos);

        if (direction == ResizeDirection.None)
            return;

        _isResizing = true;
        _resizeDirection = direction;
        _startScreenPos = PointToScreen(pos);
        _startLeft = Left;
        _startTop = Top;
        _startWidth = Width;
        _startHeight = Height;

        CaptureMouse();
        e.Handled = true;
    }

    // ============ 鼠标抬起：结束缩放 ============
    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing)
            return;

        _isResizing = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    // ============ 缩放位移计算 ============
    private void HandleResizeMove(MouseEventArgs e)
    {
        var currentScreenPos = PointToScreen(e.GetPosition(this));
        double deltaX = currentScreenPos.X - _startScreenPos.X;
        double deltaY = currentScreenPos.Y - _startScreenPos.Y;

        double newLeft = _startLeft;
        double newTop = _startTop;
        double newWidth = _startWidth;
        double newHeight = _startHeight;

        switch (_resizeDirection)
        {
            case ResizeDirection.Left:
                (newWidth, newLeft) = ResizeLeftEdge(deltaX);
                break;
            case ResizeDirection.Right:
                newWidth = Math.Max(MinWidth, _startWidth + deltaX);
                break;
            case ResizeDirection.Top:
                (newHeight, newTop) = ResizeTopEdge(deltaY);
                break;
            case ResizeDirection.Bottom:
                newHeight = Math.Max(MinHeight, _startHeight + deltaY);
                break;
            case ResizeDirection.TopLeft:
                (newWidth, newLeft) = ResizeLeftEdge(deltaX);
                (newHeight, newTop) = ResizeTopEdge(deltaY);
                break;
            case ResizeDirection.TopRight:
                newWidth = Math.Max(MinWidth, _startWidth + deltaX);
                (newHeight, newTop) = ResizeTopEdge(deltaY);
                break;
            case ResizeDirection.BottomLeft:
                (newWidth, newLeft) = ResizeLeftEdge(deltaX);
                newHeight = Math.Max(MinHeight, _startHeight + deltaY);
                break;
            case ResizeDirection.BottomRight:
                newWidth = Math.Max(MinWidth, _startWidth + deltaX);
                newHeight = Math.Max(MinHeight, _startHeight + deltaY);
                break;
        }

        Left = newLeft;
        Top = newTop;
        Width = newWidth;
        Height = newHeight;
    }

    private (double newWidth, double newLeft) ResizeLeftEdge(double deltaX)
    {
        double desired = _startWidth - deltaX;
        if (desired >= MinWidth)
            return (desired, _startLeft + deltaX);

        double clampedWidth = MinWidth;
        double clampedLeft = _startLeft + (_startWidth - MinWidth);
        return (clampedWidth, clampedLeft);
    }

    private (double newHeight, double newTop) ResizeTopEdge(double deltaY)
    {
        double desired = _startHeight - deltaY;
        if (desired >= MinHeight)
            return (desired, _startTop + deltaY);

        double clampedHeight = MinHeight;
        double clampedTop = _startTop + (_startHeight - MinHeight);
        return (clampedHeight, clampedTop);
    }

    // ============ 边缘方向检测 ============
    private ResizeDirection GetResizeDirection(Point mousePos)
    {
        double w = ActualWidth;
        double h = ActualHeight;

        bool nearLeft = mousePos.X <= ResizeBorderThickness;
        bool nearRight = mousePos.X >= w - ResizeBorderThickness;
        bool nearTop = mousePos.Y <= ResizeBorderThickness;
        bool nearBottom = mousePos.Y >= h - ResizeBorderThickness;

        if (nearTop && nearLeft) return ResizeDirection.TopLeft;
        if (nearTop && nearRight) return ResizeDirection.TopRight;
        if (nearBottom && nearLeft) return ResizeDirection.BottomLeft;
        if (nearBottom && nearRight) return ResizeDirection.BottomRight;
        if (nearTop) return ResizeDirection.Top;
        if (nearBottom) return ResizeDirection.Bottom;
        if (nearLeft) return ResizeDirection.Left;
        if (nearRight) return ResizeDirection.Right;

        return ResizeDirection.None;
    }

    // ============ 光标切换 ============
    private static void SetResizeCursor(ResizeDirection direction)
    {
        Mouse.OverrideCursor = direction switch
        {
            ResizeDirection.Left or ResizeDirection.Right => Cursors.SizeWE,
            ResizeDirection.Top or ResizeDirection.Bottom => Cursors.SizeNS,
            ResizeDirection.TopLeft or ResizeDirection.BottomRight => Cursors.SizeNWSE,
            ResizeDirection.TopRight or ResizeDirection.BottomLeft => Cursors.SizeNESW,
            _ => null
        };
    }

    private enum ResizeDirection
    {
        None,
        Left, Right, Top, Bottom,
        TopLeft, TopRight, BottomLeft, BottomRight
    }
}
