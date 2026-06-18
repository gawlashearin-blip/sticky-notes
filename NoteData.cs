namespace StickyNotes;

/// <summary>
/// 一张便签的完整持久化数据。
/// </summary>
public class NoteData
{
    public string Id { get; set; } = "";
    public string XamlContent { get; set; } = "";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }          // 折叠前的原始高度
    public string BgColor { get; set; } = "#E8C547";
    public string TitleBarColor { get; set; } = "#C9A820";
    public bool IsPinned { get; set; }
    public bool IsCollapsed { get; set; }
}
