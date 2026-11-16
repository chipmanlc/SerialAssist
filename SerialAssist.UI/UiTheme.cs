using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SerialAssist.UI;

/// <summary>全局视觉主题:深色仪器风配色、字体与通用控件样式。</summary>
internal static class UiTheme
{
    public static readonly Color Bg = Color.FromArgb(11, 15, 20);
    public static readonly Color BgElevated = Color.FromArgb(15, 20, 27);
    public static readonly Color Surface = Color.FromArgb(21, 28, 36);
    public static readonly Color SurfaceAlt = Color.FromArgb(27, 35, 45);
    public static readonly Color SurfaceHover = Color.FromArgb(35, 45, 57);
    public static readonly Color ReceiveBg = Color.FromArgb(8, 12, 17);
    public static readonly Color Border = Color.FromArgb(43, 54, 68);
    public static readonly Color BorderSoft = Color.FromArgb(31, 41, 53);
    public static readonly Color Text = Color.FromArgb(230, 237, 243);
    public static readonly Color TextSecondary = Color.FromArgb(169, 183, 198);
    public static readonly Color TextMuted = Color.FromArgb(110, 127, 145);
    public static readonly Color Accent = Color.FromArgb(45, 212, 191);
    public static readonly Color AccentHover = Color.FromArgb(94, 234, 212);
    public static readonly Color AccentPressed = Color.FromArgb(20, 184, 166);
    public static readonly Color AccentSoft = Color.FromArgb(18, 53, 50);
    public static readonly Color OnAccent = Color.FromArgb(4, 17, 15);
    public static readonly Color Success = Color.FromArgb(74, 222, 128);
    public static readonly Color Warning = Color.FromArgb(251, 191, 36);
    public static readonly Color Danger = Color.FromArgb(248, 113, 113);
    public static readonly Color Info = Color.FromArgb(96, 165, 250);

    public static Font UiFont(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style);

    public static Font MonoFont(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new("Consolas", size, style);

    /// <summary>把旧代码里的系统色映射到深色主题中可读的状态色。</summary>
    public static Color StatusColor(Color color)
    {
        if (color == Color.Firebrick)
        {
            return Danger;
        }
        if (color == Color.DarkOrange)
        {
            return Warning;
        }
        if (color == Color.ForestGreen)
        {
            return Success;
        }
        if (color == Color.DimGray || color == Color.Gray)
        {
            return TextMuted;
        }
        if (color == Color.Black)
        {
            return Text;
        }
        return color;
    }
    public static Label Label(string text, float size = 9.5f, Color? color = null, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiFont(size, style),
            ForeColor = color ?? TextSecondary,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
    }

    public static Label SectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiFont(7.5f, FontStyle.Bold),
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
    }

    public static void StyleTextBox(TextBox box, bool mono = false)
    {
        box.BackColor = SurfaceAlt;
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = mono ? MonoFont(10f) : UiFont(9.5f);
    }

    public static void StyleCombo(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = SurfaceAlt;
        combo.ForeColor = Text;
        combo.Font = UiFont(9f);
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 24;
        void ApplyDarkComboTheme()
        {
            try
            {
                SetWindowTheme(combo.Handle, string.Empty, string.Empty);
                combo.Invalidate();
            }
            catch
            {
                // 老系统没有 DarkMode_Explorer 时保留系统外观。
            }
        }
        if (combo.IsHandleCreated)
        {
            ApplyDarkComboTheme();
        }
        else
        {
            combo.HandleCreated += (_, _) => ApplyDarkComboTheme();
        }        combo.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }
            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var back = new SolidBrush(selected ? SurfaceHover : SurfaceAlt);
            e.Graphics.FillRectangle(back, e.Bounds);
            var text = combo.Items[e.Index]?.ToString() ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, selected ? Text : TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    public static void StyleRichTextBox(RichTextBox box)
    {
        box.BackColor = ReceiveBg;
        box.ForeColor = Color.FromArgb(214, 226, 235);
        box.BorderStyle = BorderStyle.None;
    }

    public static Panel Divider(DockStyle dock = DockStyle.Top, int thickness = 1)
    {
        return new Panel { Dock = dock, Height = thickness, BackColor = BorderSoft };
    }

    public static void StyleForm(Form form)
    {
        form.BackColor = Bg;
        form.ForeColor = Text;
        form.Font = UiFont();
        ApplyDarkTitleBar(form);
    }

    public static void ApplyDarkTitleBar(Form form)
    {
        void Apply()
        {
            try
            {
                var value = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
                }
            }
            catch
            {
                // 旧系统不支持深色标题栏时保持系统默认,不影响功能。
            }
        }

        if (form.IsHandleCreated)
        {
            Apply();
        }
        else
        {
            form.HandleCreated += (_, _) => Apply();
        }
    }

    /// <summary>取最近的实色父容器背景,用于自绘控件清理圆角外的底色。</summary>
    public static Color ParentBackColor(Control control)
    {
        var parent = control.Parent;
        while (parent is not null)
        {
            if (parent.BackColor != Color.Transparent && parent.BackColor.A == 255)
            {
                return parent.BackColor;
            }
            parent = parent.Parent;
        }
        return Bg;
    }
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);
}

internal enum ThemedButtonKind
{
    Primary,
    Secondary,
    Outline,
    Ghost,
    Danger,
}

/// <summary>自绘圆角按钮:统一深色主题下的悬停/按下/禁用状态。</summary>
internal sealed class ThemedButton : Button
{
    private bool _hover;
    private bool _pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemedButtonKind Kind { get; set; } = ThemedButtonKind.Secondary;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont(9f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 32;
        Margin = new Padding(0);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(UiTheme.ParentBackColor(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill;
        Color fore;
        Color border;
        switch (Kind)
        {
            case ThemedButtonKind.Primary:
                fill = !Enabled ? UiTheme.AccentSoft : _pressed ? UiTheme.AccentPressed : _hover ? UiTheme.AccentHover : UiTheme.Accent;
                fore = !Enabled ? UiTheme.TextMuted : UiTheme.OnAccent;
                border = Color.Transparent;
                break;
            case ThemedButtonKind.Outline:
                fill = !Enabled ? UiTheme.Surface : _pressed ? UiTheme.SurfaceAlt : _hover ? Color.FromArgb(46, 59, 74) : UiTheme.SurfaceHover;
                fore = !Enabled ? UiTheme.TextMuted : UiTheme.Text;
                border = _hover && Enabled ? UiTheme.Accent : Color.FromArgb(58, 73, 90);
                break;
            case ThemedButtonKind.Ghost:
                fill = _hover && Enabled ? UiTheme.SurfaceAlt : BackColor;
                fore = !Enabled ? UiTheme.TextMuted : _hover ? UiTheme.Text : UiTheme.TextSecondary;
                border = Color.Transparent;
                break;
            case ThemedButtonKind.Danger:
                fill = _pressed ? Color.FromArgb(70, 30, 36) : _hover ? Color.FromArgb(58, 27, 33) : Color.FromArgb(38, 22, 27);
                fore = !Enabled ? UiTheme.TextMuted : UiTheme.Danger;
                border = Color.FromArgb(92, 42, 50);
                break;
            default:
                fill = !Enabled ? UiTheme.Surface : _pressed ? UiTheme.Surface : _hover ? UiTheme.SurfaceHover : UiTheme.SurfaceAlt;
                fore = !Enabled ? UiTheme.TextMuted : UiTheme.Text;
                border = !Enabled ? UiTheme.BorderSoft : _hover ? Color.FromArgb(78, 96, 116) : UiTheme.Border;
                break;
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, CornerRadius);
        if (fill != Color.Transparent)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        if (border != Color.Transparent)
        {
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>圆角卡片容器:可指定外底色、填充色和边框色。</summary>
internal class ThemedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 12;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = UiTheme.Surface;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = UiTheme.BorderSoft;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderWidth { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OuterColor { get; set; } = UiTheme.Bg;

    public ThemedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(OuterColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, CornerRadius);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);
        if (BorderWidth > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

/// <summary>圆角可选标签,替代系统 RadioButton 的白色圆点。</summary>
internal abstract class ChipBoxBase : ButtonBase
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public abstract bool Checked { get; set; }
    private bool _hover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CheckedFill { get; set; } = UiTheme.AccentSoft;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CheckedText { get; set; } = UiTheme.Accent;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CheckedBorder { get; set; } = Color.FromArgb(42, 100, 92);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 7;

    protected ChipBoxBase()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 28;
        Font = UiTheme.UiFont(8.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Width = TextRenderer.MeasureText(Text, Font).Width + 24;
        Invalidate();
    }

    protected virtual void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(UiTheme.ParentBackColor(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var checkedState = Checked;
        var fill = checkedState ? CheckedFill : _hover ? UiTheme.SurfaceHover : UiTheme.SurfaceAlt;
        var border = checkedState ? CheckedBorder : UiTheme.BorderSoft;
        var fore = checkedState ? CheckedText : _hover ? UiTheme.Text : UiTheme.TextSecondary;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, CornerRadius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        using var pen = new Pen(border);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class ChipCheckBox : ChipBoxBase
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }
            _checked = value;
            OnCheckedChanged(EventArgs.Empty);
        }
    }

    private bool _checked;

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    public event EventHandler? CheckedChanged;

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        CheckedChanged?.Invoke(this, e);
    }
}

internal sealed class ChipRadio : ChipBoxBase
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }
            if (value)
            {
                UncheckSiblings();
            }
            _checked = value;
            OnCheckedChanged(EventArgs.Empty);
        }
    }

    private bool _checked;

    private void UncheckSiblings()
    {
        if (Parent is null)
        {
            return;
        }
        foreach (var sibling in Parent.Controls.OfType<ChipRadio>())
        {
            if (sibling != this && sibling.Checked)
            {
                sibling.Checked = false;
            }
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Checked)
        {
            Checked = true;
        }
        base.OnClick(e);
    }

    public event EventHandler? CheckedChanged;

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        CheckedChanged?.Invoke(this, e);
    }
}

/// <summary>拨动开关,保留 CheckBox 的 Checked 语义与事件。</summary>
internal sealed class SwitchCheckBox : CheckBox
{
    private bool _hover;

    public SwitchCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 28;
        Font = UiTheme.UiFont(8.5f);
        ForeColor = UiTheme.TextSecondary;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Width = TextRenderer.MeasureText(Text, Font).Width + 50;
        Invalidate();
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(UiTheme.ParentBackColor(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int trackW = 32;
        const int trackH = 18;
        var track = new Rectangle(0, (Height - trackH) / 2, trackW, trackH);
        using (var path = UiTheme.RoundedRect(track, trackH / 2))
        using (var brush = new SolidBrush(Checked ? UiTheme.Accent : _hover ? UiTheme.SurfaceHover : UiTheme.SurfaceAlt))
        {
            g.FillPath(brush, path);
            if (!Checked)
            {
                using var pen = new Pen(UiTheme.Border);
                g.DrawPath(pen, path);
            }
        }
        var knobX = Checked ? track.Right - 15 : track.Left + 3;
        using (var knob = new SolidBrush(Checked ? UiTheme.OnAccent : UiTheme.TextMuted))
        {
            g.FillEllipse(knob, knobX, track.Top + 3, 12, 12);
        }
        var textRect = new Rectangle(trackW + 10, 0, Width - trackW - 10, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Checked ? UiTheme.Text : UiTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
/// <summary>自绘下拉选择框:完全绕过系统 ComboBox 的白色底,支持键盘与下拉菜单。</summary>
internal sealed class ThemedSelect : Control
{
    private readonly List<string> _items = new();
    private int _selectedIndex = -1;
    private bool _hover;
    private bool _menuOpen;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Items => _items;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value < -1 || value >= _items.Count ? -1 : value;
            if (_selectedIndex == next)
            {
                return;
            }
            _selectedIndex = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedItem => _selectedIndex >= 0 ? _items[_selectedIndex] : null;

    public event EventHandler? SelectedIndexChanged;

    public ThemedSelect()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        Height = 28;
        Font = UiTheme.UiFont(9f);
        ForeColor = UiTheme.Text;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(0);
    }

    public void AddRange(params string[] items)
    {
        _items.AddRange(items);
        if (_selectedIndex < 0 && _items.Count > 0)
        {
            _selectedIndex = 0;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(UiTheme.ParentBackColor(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, 7);
        using var fill = new SolidBrush(!Enabled ? UiTheme.Surface : _hover ? UiTheme.SurfaceHover : UiTheme.SurfaceAlt);
        g.FillPath(fill, path);
        using var border = new Pen(Focused ? UiTheme.Accent : UiTheme.Border);
        g.DrawPath(border, path);

        var text = SelectedItem ?? string.Empty;
        var textRect = new Rectangle(10, 0, Width - 30, Height);
        TextRenderer.DrawText(g, text, Font, textRect,
            Enabled ? (string.IsNullOrEmpty(text) ? UiTheme.TextMuted : UiTheme.Text) : UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "▾", Font, new Rectangle(Width - 24, 0, 18, Height),
            UiTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        OpenMenu();
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Up)
        {
            if (_items.Count > 0)
            {
                var delta = e.KeyCode == Keys.Down ? 1 : -1;
                SelectedIndex = Math.Clamp(_selectedIndex + delta, 0, _items.Count - 1);
            }
            e.Handled = true;
            return;
        }
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OpenMenu();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OpenMenu()
    {
        if (!Enabled || _menuOpen || _items.Count == 0)
        {
            return;
        }
        _menuOpen = true;
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = UiTheme.SurfaceAlt,
            ForeColor = UiTheme.Text,
            Font = UiTheme.UiFont(9f),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
        };
        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(_items[i])
            {
                Checked = i == _selectedIndex,
                CheckOnClick = false,
                ForeColor = UiTheme.Text,
                BackColor = UiTheme.SurfaceAlt,
            };
            item.Click += (_, _) => SelectedIndex = index;
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            try
            {
                BeginInvoke(new Action(menu.Dispose));
            }
            catch (InvalidOperationException)
            {
                menu.Dispose();
            }
        };
        menu.Show(this, new Point(0, Height));
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiTheme.SurfaceAlt;
        public override Color MenuItemSelected => UiTheme.SurfaceHover;
        public override Color MenuItemBorder => UiTheme.Border;
        public override Color MenuBorder => UiTheme.Border;
        public override Color ImageMarginGradientBegin => UiTheme.SurfaceAlt;
        public override Color ImageMarginGradientMiddle => UiTheme.SurfaceAlt;
        public override Color ImageMarginGradientEnd => UiTheme.SurfaceAlt;
    }
}