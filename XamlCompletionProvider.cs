using CodeForge;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeForgeDemo
{
    /// <summary>
    /// XAML 提示引擎（演示级）：静态词表 + 光标上下文三级判定，产标准 CodeCompletionItem
    /// （走 CodeEditorControl.ExternalCompletionProvider 扩展点，弹层/过滤/采纳复用 C# 管线）。
    ///   ① "&lt;" 后                     → WPF 元素名（Class / 右侧"元素"）
    ///   ② 标签内空白后（含 "&lt;Grid "）  → 元素特有属性 + 通用属性 + 附加属性（Property / 右侧"属性"）
    ///   ③ 属性 =" 引号内                → 常见取值（EnumMember / 右侧"取值"）
    /// 词表为演示用静态数据；DesignerKit 正式版换成程序集反射 + xmlns 解析。
    /// </summary>
    internal static class XamlCompletionProvider
    {
        /// <summary>给 CodeEditorControl.ExternalCompletionProvider 的入口（全文 + 光标 → 词表或 null）。</summary>
        public static List<CodeCompletionItem>? GetForEditor(string text, int caret)
            => GetCandidates(text, caret);

        // —— 通用属性（几乎所有元素都适用）——
        private static readonly (string Name, string Desc)[] CommonAttributes =
        {
            ("Name", "元素名（代码引用 / 注册进 NameScope）"),
            ("x:Name", "XAML 命名（可带点号作用域）"),
            ("Width", "宽度（设备无关像素）"),
            ("Height", "高度"),
            ("MinWidth", "最小宽度"),
            ("MinHeight", "最小高度"),
            ("Margin", "外边距，如 \"10\" 或 \"10,5\""),
            ("Padding", "内边距"),
            ("HorizontalAlignment", "水平对齐"),
            ("VerticalAlignment", "垂直对齐"),
            ("Background", "背景（颜色 / 画刷资源）"),
            ("Foreground", "前景色"),
            ("FontSize", "字号（像素）"),
            ("FontWeight", "字重"),
            ("Visibility", "可见性"),
            ("IsEnabled", "是否启用"),
            ("Opacity", "不透明度 0~1"),
            ("ToolTip", "悬停提示文本"),
            ("Tag", "任意附加数据"),
            ("Cursor", "鼠标指针"),
        };

        // —— 附加属性（前缀形式）——
        private static readonly string[] AttachedProperties =
        {
            "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
            "Canvas.Left", "Canvas.Top", "Canvas.Right", "Canvas.Bottom",
            "Canvas.ZIndex", "DockPanel.Dock", "ScrollViewer.HorizontalScrollBarVisibility",
            "ScrollViewer.VerticalScrollBarVisibility",
        };

        // —— 常用元素及其特有属性 ——
        private static readonly Dictionary<string, (string Elem, string Desc, (string, string)[] Attrs)[]> _catalog
            = new()
        {
            ["layout"] = (new (string, string, (string, string)[])[]
            {
                ("Grid", "网格布局：行/列定义 + 附加属性定位",
                    new[] { ("RowDefinitions", "行定义，如 \"Auto,*,2*\""), ("ColumnDefinitions", "列定义"), ("ShowGridLines", "显示网格虚线") }),
                ("StackPanel", "堆叠布局（水平/垂直）",
                    new[] { ("Orientation", "排列方向") }),
                ("DockPanel", "停靠布局",
                    new[] { ("LastChildFill", "最后子元素填满") }),
                ("WrapPanel", "流式换行布局",
                    new[] { ("Orientation", "排列方向"), ("ItemWidth", "统一项宽"), ("ItemHeight", "统一项高") }),
                ("UniformGrid", "均分网格",
                    new[] { ("Rows", "行数"), ("Columns", "列数") }),
                ("Canvas", "绝对坐标画布", Array.Empty<(string, string)>()),
                ("Border", "边框容器（圆角/背景）",
                    new[] { ("CornerRadius", "圆角"), ("BorderThickness", "边框粗细"), ("BorderBrush", "边框颜色") }),
                ("ScrollViewer", "滚动容器",
                    new[] { ("HorizontalScrollBarVisibility", "横向滚动条"), ("VerticalScrollBarVisibility", "纵向滚动条") }),
                ("Viewbox", "等比缩放容器",
                    new[] { ("Stretch", "缩放模式") }),
                ("Expander", "折叠面板",
                    new[] { ("Header", "标题内容"), ("IsExpanded", "是否展开") }),
                ("TabControl", "选项卡容器", Array.Empty<(string, string)>()),
                ("GroupBox", "分组框",
                    new[] { ("Header", "分组标题") }),
            }),
            ["control"] = (new (string, string, (string, string)[])[]
            {
                ("Button", "按钮",
                    new[] { ("Content", "按钮内容"), ("Command", "绑定命令"), ("CommandParameter", "命令参数"), ("Click", "点击事件处理器") }),
                ("TextBlock", "文本显示（不可编辑）",
                    new[] { ("Text", "文本内容"), ("TextWrapping", "换行"), ("TextTrimming", "省略号"), ("TextAlignment", "对齐") }),
                ("TextBox", "文本输入",
                    new[] { ("Text", "文本内容"), ("TextWrapping", "换行"), ("AcceptsReturn", "接受回车"), ("IsReadOnly", "只读"), ("MaxLength", "最大字符数"), ("TextChanged", "文本变更事件") }),
                ("CheckBox", "复选框",
                    new[] { ("Content", "标签文本"), ("IsChecked", "选中状态"), ("Checked", "选中事件"), ("Unchecked", "取消选中事件") }),
                ("RadioButton", "单选钮",
                    new[] { ("Content", "标签文本"), ("GroupName", "分组名（同组互斥）"), ("IsChecked", "选中状态") }),
                ("ComboBox", "下拉框",
                    new[] { ("ItemsSource", "数据源绑定"), ("SelectedItem", "选中项绑定"), ("SelectedIndex", "选中索引"), ("IsEditable", "可编辑"), ("DisplayMemberPath", "显示成员路径") }),
                ("ListBox", "列表",
                    new[] { ("ItemsSource", "数据源绑定"), ("SelectedItem", "选中项"), ("SelectionMode", "选择模式") }),
                ("ListView", "列表视图（可列头）", Array.Empty<(string, string)>()),
                ("Slider", "滑块",
                    new[] { ("Minimum", "最小值"), ("Maximum", "最大值"), ("Value", "当前值"), ("TickFrequency", "刻度间隔"), ("IsSnapToTickEnabled", "吸附刻度") }),
                ("ProgressBar", "进度条",
                    new[] { ("Minimum", "最小值"), ("Maximum", "最大值"), ("Value", "当前值"), ("IsIndeterminate", "不确定模式") }),
                ("Image", "图片",
                    new[] { ("Source", "图片源（路径/资源/绑定）"), ("Stretch", "拉伸模式") }),
                ("Label", "标签（支持 Target 助记键）",
                    new[] { ("Content", "标签文本"), ("Target", "助记键绑定的控件名") }),
                ("ToggleButton", "切换按钮",
                    new[] { ("Content", "按钮内容"), ("IsChecked", "切换状态") }),
                ("Menu", "菜单栏", Array.Empty<(string, string)>()),
                ("ContextMenu", "右键菜单", Array.Empty<(string, string)>()),
                ("ToolBar", "工具栏", Array.Empty<(string, string)>()),
                ("StatusBar", "状态栏", Array.Empty<(string, string)>()),
                ("Calendar", "日历控件", Array.Empty<(string, string)>()),
                ("DatePicker", "日期选择",
                    new[] { ("SelectedDate", "选中日期绑定"), ("DisplayDateStart", "可选起始"), ("DisplayDateEnd", "可选结束") }),
                ("RichTextBox", "富文本编辑", Array.Empty<(string, string)>()),
            }),
            // HandyControl 控件（hc: 前缀态列出；词表按 HC 3.5.1 常用控件静态整理，
            // 正式版由程序集反射 + xmlns 映射动态生成）。插入前根元素需声明 xmlns:hc。
            ["hc"] = (new (string, string, (string, string)[])[]
            {
                ("hc:Window", "HandyControl 主题窗口（标题栏/亮暗皮肤内置，替换 <Window 用）",
                    new[] { ("NonClientAreaContent", "自定义标题栏内容"), ("Logo", "标题栏 Logo") }),
                ("hc:BlurWindow", "模糊背景窗口（亚克力效果）", Array.Empty<(string, string)>()),
                ("hc:Badge", "徽标：元素右上角红点/数字提醒",
                    new[] { ("Badge", "角标内容（数字/文本）"), ("BadgeMargin", "角标边距") }),
                ("hc:Card", "卡片容器（圆角+阴影+头部）",
                    new[] { ("Header", "卡片头部内容") }),
                ("hc:Divider", "分隔线（带文字/方向）",
                    new[] { ("Orientation", "排列：Left / Center / Right"), ("LineStroke", "线色") }),
                ("hc:NumericUpDown", "数值输入（步进按钮）",
                    new[] { ("Value", "当前值"), ("Minimum", "最小值"), ("Maximum", "最大值"), ("Increment", "步长"), ("DecimalPlaces", "小数位") }),
                ("hc:PasswordBox", "密码输入（可切换明文）",
                    new[] { ("Password", "密码绑定") }),
                ("hc:DateTimePicker", "日期时间选择",
                    new[] { ("SelectedDateTime", "选中值绑定") }),
                ("hc:ColorPicker", "颜色选择器",
                    new[] { ("SelectedBrush", "选中颜色绑定"), ("IsEnabled", "是否启用") }),
                ("hc:Rate", "评分",
                    new[] { ("Value", "当前评分"), ("Count", "星数"), ("AllowClear", "可取消：True/False") }),
                ("hc:Pagination", "分页",
                    new[] { ("PageIndex", "当前页"), ("MaxPageCount", "总页数"), ("IsJumpEnabled", "跳页：True/False") }),
                ("hc:StepBar", "步骤条",
                    new[] { ("StepIndex", "当前步骤"), ("StepCount", "总步数") }),
                ("hc:SideMenu", "侧边菜单", Array.Empty<(string, string)>()),
                ("hc:SearchBar", "搜索栏（带搜索按钮）",
                    new[] { ("Text", "搜索词绑定"), ("IsRealTime", "实时搜索：True/False") }),
                ("hc:Tag", "标签（可关闭）",
                    new[] { ("Content", "标签文本"), ("ShowCloseButton", "显示关闭：True/False") }),
                ("hc:Transfer", "穿梭框",
                    new[] { ("ItemsSource", "数据源"), ("SelectedItems", "已选集合") }),
                ("hc:LoadingLine", "线形加载动画", Array.Empty<(string, string)>()),
                ("hc:LoadingCircle", "圆形加载动画", Array.Empty<(string, string)>()),
                ("hc:Shield", "徽章盾牌（如版本号展示）",
                    new[] { ("Subject", "左侧文本"), ("Status", "右侧文本"), ("Color", "右侧底色") }),
                ("hc:SplitButton", "拆分按钮（按钮+下拉）",
                    new[] { ("Content", "按钮内容"), ("IsDropDownOpen", "下拉展开") }),
                ("hc:GoToTop", "回到顶部悬浮钮",
                    new[] { ("TargetScrollViewer", "目标滚动容器绑定") }),
                ("hc:WaterfallLabel", "瀑布流文本标签",
                    new[] { ("Text", "文本内容"), ("DeviceSize", "响应式档位") }),
            }),
        };

        // —— 属性常见取值 ——
        private static readonly Dictionary<string, string[]> _values = new(StringComparer.Ordinal)
        {
            ["HorizontalAlignment"] = new[] { "Left", "Center", "Right", "Stretch" },
            ["VerticalAlignment"] = new[] { "Top", "Center", "Bottom", "Stretch" },
            ["HorizontalContentAlignment"] = new[] { "Left", "Center", "Right", "Stretch" },
            ["VerticalContentAlignment"] = new[] { "Top", "Center", "Bottom", "Stretch" },
            ["Orientation"] = new[] { "Horizontal", "Vertical" },
            ["Visibility"] = new[] { "Visible", "Hidden", "Collapsed" },
            ["IsEnabled"] = new[] { "True", "False" },
            ["IsChecked"] = new[] { "True", "False" },
            ["IsReadOnly"] = new[] { "True", "False" },
            ["IsExpanded"] = new[] { "True", "False" },
            ["AcceptsReturn"] = new[] { "True", "False" },
            ["ShowGridLines"] = new[] { "True", "False" },
            ["LastChildFill"] = new[] { "True", "False" },
            ["IsIndeterminate"] = new[] { "True", "False" },
            ["IsEditable"] = new[] { "True", "False" },
            ["IsSnapToTickEnabled"] = new[] { "True", "False" },
            ["TextWrapping"] = new[] { "Wrap", "NoWrap", "WrapWithOverflow" },
            ["TextTrimming"] = new[] { "None", "CharacterEllipsis", "WordEllipsis" },
            ["TextAlignment"] = new[] { "Left", "Center", "Right", "Justify" },
            ["Stretch"] = new[] { "Uniform", "Fill", "UniformToFill", "None" },
            ["SelectionMode"] = new[] { "Single", "Multiple", "Extended" },
            ["Cursor"] = new[] { "Arrow", "Hand", "Wait", "IBeam", "Cross", "SizeNS" },
            ["FontWeight"] = new[] { "Normal", "Light", "SemiBold", "Bold", "ExtraBold" },
            ["DockPanel.Dock"] = new[] { "Left", "Top", "Right", "Bottom" },
            ["ScrollViewer.HorizontalScrollBarVisibility"] = new[] { "Auto", "Disabled", "Hidden", "Visible" },
            ["ScrollViewer.VerticalScrollBarVisibility"] = new[] { "Auto", "Disabled", "Hidden", "Visible" },
            // 尺寸类：VS 对 Width/Height 首推 Auto（数值/星号手动输入）
            ["Width"] = new[] { "Auto" },
            ["Height"] = new[] { "Auto" },
            ["MinWidth"] = new[] { "Auto" },
            ["MinHeight"] = new[] { "Auto" },
        };

        private static CodeCompletionItem Item(string text, CompletionItemKind kind, string right, string desc,
            string? insertText = null)
            => new()
            {
                DisplayText = text,
                InsertText = insertText ?? text,
                FilterText = text,
                Kind = kind,
                RightSignature = right,
                Description = desc,
            };

        /// <summary>依据光标上下文生成候选；null 表示无候选（不弹）。</summary>
        public static List<CodeCompletionItem>? GetCandidates(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret <= 0) return null;
            var left = text.Substring(0, caret);

            // ③b '=' 直后（还没引号）：XAML 惯例 —— 打完 = 立即提示取值，选完写入 ="值"。
            //    输入过滤仍按裸值名（FilterText），采纳用带引号的 InsertText。
            var eqCtx = TryAttributeValueAfterEquals(left);
            if (eqCtx != null)
            {
                if (eqCtx == string.Empty || !_values.TryGetValue(eqCtx, out var vals0))
                    return null;
                return vals0.Select(v => Item(v, CompletionItemKind.EnumMember, "取值", $"属性 {eqCtx} 的取值",
                    insertText: "\"" + v + "\"")).ToList();   // '=' 用户已敲，只补 "值"
            }

            // ③ 属性值：光标前最近的 = 后、未闭合引号前
            var valueCtx = TryAttributeValue(left);
            if (valueCtx != null)
            {
                if (valueCtx == string.Empty || !_values.TryGetValue(valueCtx, out var vals))
                    return null;
                return vals.Select(v => Item(v, CompletionItemKind.EnumMember, "取值", $"属性 {valueCtx} 的取值")).ToList();
            }

            // ② 标签内：光标在属性名位置。先看"前缀."态（Grid. → Row/Column…），再给常规属性表
            if (InsideTagAttributePosition(left, out var tagName))
            {
                var dotted = TryAttachedAfterDot(left);
                if (dotted != null) return dotted;
                return BuildAttributeCandidates(tagName);
            }

            // ① 元素名：'<' 紧邻左侧、正在输入名字（仅字母/数字/_，不含量点）→ 元素候选
            //    带冒号 = 命名空间前缀态：**文档里声明了 xmlns 才提示**（跟 using 一个道理）——
            //    xmlns 值含 handycontrol → HC 组；声明了但不是控件命名空间（如 x:）→ 不弹；
            //    没声明的前缀 → 不弹。带 '.'（如 <Grid.）= 非法 → 不弹。
            int lt = left.LastIndexOf('<');
            if (lt >= 0)
            {
                var between = left.Substring(lt + 1);
                if (between.Length == 0)
                {
                    _lastElementPrefix = null;
                    return BuildElementCandidates(null);
                }
                if (between.Length <= 16 && IsElementNameChars(between))
                {
                    if (!between.Contains(':'))
                    {
                        _lastElementPrefix = between;
                        var plain = BuildElementCandidates(null);
                        _lastElementPrefix = null;
                        return plain;
                    }
                    var prefix = between.Substring(0, between.IndexOf(':'));
                    if (!IsPrefixDeclared(text, prefix)) return null;
                    if (!IsHandyControlPrefix(text, prefix)) return null;
                    return BuildElementCandidates(prefix);
                }
            }

            return null;
        }

        /// <summary>
        /// 属性名位打的是"前缀."态（Grid. / Grid.R）→ 只给该前缀的附加属性短名
        /// （Row / Column…，"Grid." 已在稿上不重复）；无此前缀的附加属性 → null（不弹）。
        /// </summary>
        private static List<CodeCompletionItem>? TryAttachedAfterDot(string left)
        {
            var tail = WordBeforeCaret(left);
            if (tail == null || !tail.Contains('.')) return null;

            int dot = tail.LastIndexOf('.');
            var attachedPrefix = tail.Substring(0, dot);

            var names = new List<string>();
            foreach (var ap in AttachedProperties)
                if (ap.StartsWith(attachedPrefix + ".", StringComparison.Ordinal))
                    names.Add(ap.Substring(attachedPrefix.Length + 1));
            foreach (var k in _values.Keys)
                if (k.Contains('.') && k.StartsWith(attachedPrefix + ".", StringComparison.Ordinal) &&
                    !names.Contains(k.Substring(attachedPrefix.Length + 1)))
                    names.Add(k.Substring(attachedPrefix.Length + 1));

            if (names.Count == 0) return null;
            return names.OrderBy(n => n, StringComparer.Ordinal)
                        .Select(n => Item(n, CompletionItemKind.Property, "附加属性", $"{attachedPrefix}.{n} 附加属性"))
                        .ToList();
        }

        /// <summary>光标前的词（标识符字符 + 量点）；一个都没有返回 null。</summary>
        private static string? WordBeforeCaret(string left)
        {
            int i = left.Length;
            while (i > 0 && (char.IsLetterOrDigit(left[i - 1]) || left[i - 1] == '_' || left[i - 1] == '.'))
                i--;
            return i == left.Length ? null : left.Substring(i);
        }

        /// <summary>文档里是否声明了 xmlns:{prefix}=（根元素或任意位置）。</summary>
        private static bool IsPrefixDeclared(string text, string prefix)
            => text.IndexOf("xmlns:" + prefix + "=", StringComparison.Ordinal) >= 0;

        /// <summary>声明的前缀是否指向 HandyControl 命名空间（xmlns 值含 handycontrol）。</summary>
        private static bool IsHandyControlPrefix(string text, string prefix)
        {
            int i = text.IndexOf("xmlns:" + prefix + "=", StringComparison.Ordinal);
            if (i < 0) return false;
            int q1 = text.IndexOf('"', i);
            int q2 = q1 >= 0 ? text.IndexOf('"', q1 + 1) : -1;
            if (q2 < 0) return false;
            return text.IndexOf("handycontrol", q1, q2 - q1, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>元素名局部字符：字母/数字/下划线/冒号（前缀），不含 '.'。</summary>
        private static bool IsElementNameChars(string s)
            => s.All(c => char.IsLetterOrDigit(c) || c == '_' || c == ':');

        /// <summary>
        /// 光标紧跟 '='（或 '=' 后未闭合的部分值名）：返回属性名（不在该形态返回 null，无词表返回空串）。
        /// </summary>
        private static string? TryAttributeValueAfterEquals(string left)
        {
            int eq = left.LastIndexOf('=');
            if (eq < 0) return null;
            var afterEq = left.Substring(eq + 1);
            if (afterEq.Length > 0 && !IsNameChars(afterEq)) return null;   // 已有引号/杂字符 → 交给引号分支

            int start = eq;
            while (start > 0 && left[start - 1] != ' ' && left[start - 1] != '\t' && left[start - 1] != '<') start--;
            var attr = left.Substring(start, eq - start).Trim();
            if (attr.Length == 0 || !IsNameChars(attr.Replace("x:", ""))) return string.Empty;
            return _values.ContainsKey(attr) ? attr : string.Empty;
        }

        private static bool IsNameChars(string s)
            => s.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ':');

        /// <summary>光标是否处于 "属性值引号内"；返回属性名（不在引号内返回 null，在引号但无词表返回空串）。</summary>
        private static string? TryAttributeValue(string left)
        {
            int eq = left.LastIndexOf('=');
            if (eq < 0) return null;
            int q = eq + 1;
            while (q < left.Length && (left[q] == ' ' || left[q] == '\t')) q++;
            if (q >= left.Length || (left[q] != '"' && left[q] != '\'')) return null;
            char quote = left[q];
            int close = left.IndexOf(quote, q + 1);
            if (close >= 0) return null;   // 已闭合 = 不在值里

            int start = eq;
            while (start > 0 && left[start - 1] != ' ' && left[start - 1] != '\t' && left[start - 1] != '<') start--;
            var attr = left.Substring(start, eq - start).Trim();
            if (attr.Length == 0 || !IsNameChars(attr.Replace("x:", ""))) return string.Empty;
            return _values.ContainsKey(attr) ? attr : string.Empty;   // 在值位：有词表给词表，没有就别弹
        }

        /// <summary>
        /// 光标处于标签内属性名位置 → true 并输出标签名。判定（引号已成对、未出标签前提）：
        /// 取 seg 中**最后一个空白**之后的部分为尾段——尾段为空（刚敲空格）或是标识符
        /// （正在打属性名，如 &lt;TextBlock T|）都算；这样打字态列表不会被关掉。
        /// </summary>
        private static bool InsideTagAttributePosition(string left, out string tagName)
        {
            tagName = string.Empty;
            int lt = left.LastIndexOf('<');
            if (lt < 0) return false;
            var seg = left.Substring(lt + 1);

            int quotes = 0;
            foreach (var ch in seg) if (ch == '"' || ch == '\'') quotes++;
            if (quotes % 2 == 1) return false;            // 引号未闭合 = 在属性值里

            int gt = seg.IndexOf('>');
            if (gt >= 0) return false;                    // 已出标签
            int sp = seg.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            if (sp < 0) return false;                     // 还在元素名上 → 元素分支处理
            tagName = seg.Substring(0, sp);
            if (!IsNameChars(tagName)) return false;

            int lastWs = seg.LastIndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            var tail = seg.Substring(lastWs + 1);
            return tail.Length == 0 || IsNameChars(tail); // 空格后 | 打字态
        }

        /// <summary>只随命名空间前缀出现的组（hc:）：无前缀元素名不含冒号，绝不混入无前缀列表。</summary>
        private static readonly HashSet<string> PrefixOnlyGroups = new(StringComparer.Ordinal) { "hc" };

        private static List<CodeCompletionItem> BuildElementCandidates(string? prefix)
        {
            var list = new List<CodeCompletionItem>();
            // 前缀态（hc:）→ 只给该前缀组的元素
            if (prefix != null)
            {
                if (_catalog.TryGetValue(prefix, out var hcGroup))
                    foreach (var e in hcGroup)
                        list.Add(Item(e.Elem, CompletionItemKind.Class, "HC元素",
                            e.Desc + "（需根元素声明 xmlns:hc）"));
                return list;
            }
            foreach (var pair in _catalog)
            {
                if (PrefixOnlyGroups.Contains(pair.Key)) continue;   // <hc（无冒号）不该看到 hc: 项
                foreach (var e in pair.Value)
                    list.Add(Item(e.Elem, CompletionItemKind.Class, "元素", e.Desc));
            }
            list.Add(Item("Window", CompletionItemKind.Class, "元素", "WPF 窗口根元素"));
            list.Add(Item("UserControl", CompletionItemKind.Class, "元素", "用户控件根元素"));
            list.Add(Item("ResourceDictionary", CompletionItemKind.Class, "元素", "资源字典"));

            // 按已敲前缀预过滤：一个都不剩 → 不弹（如删了 xmlns:hc 后打 <hc，WPF 元素全不匹配）
            var typed = _lastElementPrefix;
            if (!string.IsNullOrEmpty(typed))
            {
                list.RemoveAll(i => !(i.FilterText ?? i.DisplayText ?? "")
                    .StartsWith(typed, StringComparison.OrdinalIgnoreCase));
                if (list.Count == 0) return null;
            }
            return list;
        }

        [ThreadStatic] private static string? _lastElementPrefix;

        private static List<CodeCompletionItem> BuildAttributeCandidates(string tagName)
        {
            var list = new List<CodeCompletionItem>();
            foreach (var group in _catalog.Values)
            {
                var hit = group.FirstOrDefault(e => e.Elem == tagName);
                if (hit.Elem == null) continue;
                foreach (var (n, d) in hit.Attrs)
                    list.Add(Item(n, CompletionItemKind.Property, "属性", d));
            }
            foreach (var (n, d) in CommonAttributes)
                list.Add(Item(n, CompletionItemKind.Property, "属性", d));
            foreach (var ap in AttachedProperties)
                list.Add(Item(ap, CompletionItemKind.Property, "附加属性", "附加属性"));
            return list;
        }
    }
}
