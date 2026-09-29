using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YoyoClawCompanion.Services;

internal sealed class TrayMenuRenderer(bool light) : ToolStripProfessionalRenderer(new TrayColorTable(light))
{
    private readonly Color _text = light ? Color.FromArgb(27, 27, 27) : Color.FromArgb(237, 237, 237);
    private readonly Color _secondary = light ? Color.FromArgb(110, 110, 110) : Color.FromArgb(154, 154, 154);
    private readonly Color _hover = light ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(23, 255, 255, 255);
    private readonly Color _dangerHover = light ? Color.FromArgb(28, 242, 104, 111) : Color.FromArgb(36, 242, 104, 111);
    private readonly Color _border = light ? Color.FromArgb(223, 223, 223) : Color.FromArgb(61, 61, 61);
    private readonly Color _separator = light ? Color.FromArgb(228, 228, 228) : Color.FromArgb(69, 69, 69);

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = Rounded(bounds, 6);
        using var brush = new SolidBrush(string.Equals(e.Item.Tag?.ToString(), "danger", StringComparison.Ordinal)
            ? _dangerHover : _hover);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled) e.TextColor = Color.FromArgb(120, _secondary);
        else if (e.Item.Selected && string.Equals(e.Item.Tag?.ToString(), "danger", StringComparison.Ordinal)) e.TextColor = Color.FromArgb(242, 104, 111);
        else e.TextColor = e.TextFormat.HasFlag(TextFormatFlags.Right) ? _secondary : _text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_separator);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TrayColorTable(bool light) : ProfessionalColorTable
{
    private readonly Color _background = light ? Color.FromArgb(250, 250, 250) : Color.FromArgb(43, 43, 43);
    public override Color ToolStripDropDownBackground => _background;
    public override Color ImageMarginGradientBegin => _background;
    public override Color ImageMarginGradientMiddle => _background;
    public override Color ImageMarginGradientEnd => _background;
    public override Color MenuBorder => light ? Color.FromArgb(223, 223, 223) : Color.FromArgb(61, 61, 61);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.Transparent;
    public override Color SeparatorDark => light ? Color.FromArgb(228, 228, 228) : Color.FromArgb(69, 69, 69);
    public override Color SeparatorLight => SeparatorDark;
}

internal static class TrayMenuGraphics
{
    public static Bitmap Dot(Color color)
    {
        var bitmap = Canvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var halo = new SolidBrush(Color.FromArgb(40, color));
        using var fill = new SolidBrush(color);
        graphics.FillEllipse(halo, 2, 2, 12, 12);
        graphics.FillEllipse(fill, 5, 5, 6, 6);
        return bitmap;
    }

    public static Bitmap LineIcon(string kind, Color color)
    {
        var bitmap = Canvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (kind)
        {
            case "home":
                graphics.DrawLines(pen, [new PointF(2, 8), new PointF(8, 3), new PointF(14, 8)]);
                graphics.DrawLines(pen, [new PointF(4, 7), new PointF(4, 14), new PointF(12, 14), new PointF(12, 7)]);
                break;
            case "show": graphics.DrawEllipse(pen, 2, 4, 12, 8); graphics.DrawEllipse(pen, 6, 6, 4, 4); break;
            case "refresh": graphics.DrawArc(pen, 2, 2, 12, 12, 35, 285); graphics.DrawLines(pen, [new PointF(12, 2), new PointF(14, 5), new PointF(10, 5)]); break;
            case "copy": graphics.DrawRectangle(pen, 5, 5, 9, 9); graphics.DrawLines(pen, [new PointF(2, 11), new PointF(2, 2), new PointF(11, 2)]); break;
            case "exit": graphics.DrawLine(pen, 8, 1, 8, 8); graphics.DrawArc(pen, 2, 3, 12, 12, -45, 270); break;
        }
        return bitmap;
    }

    private static Bitmap Canvas() => new(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
}
