#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // ← IScrollInfo（AvalonEdit TextView 实现了这个接口，有 ExtentHeight/ViewportHeight/VerticalOffset）
using System.Windows.Input;
using System.Windows.Media;
using CodeForge;
using HandyControl.Data;
using HandyControl.Themes;
using Microsoft.CodeAnalysis;
using Microsoft.Win32;

namespace CodeForgeDemo
{
    /// <summary>
    /// 错误 / 警告 列表项（ListBox 绑定用）
    /// </summary>
    public class DiagnosticItem
    {
        /// <summary>"错误" / "警告"</summary>
        public string SeverityText { get; set; } = "信息";
        /// <summary>红/黄等颜色的 Brush</summary>
        public Brush SeverityBrush { get; set; } = Brushes.Gray;
        /// <summary>显示完整描述</summary>
        public string Display { get; set; } = "";
        /// <summary>原始行号（1-based）；双击跳转用</summary>
        public int Line { get; set; }
        /// <summary>原始列号（1-based）；双击跳转用</summary>
        public int Column { get; set; }
        /// <summary>原始 Severity（用来分类计数）</summary>
        public DiagnosticSeverity Severity { get; set; }
    }

    public partial class MainWindow
    {
        // ========== 错误 / 警告 列表（DataTemplate 绑定） ==========
        // 编辑器控件 _editor 由 XAML 声明（x:Name="_editor"），含鸟瞰图/主题/字号等全部配置属性
        private readonly ObservableCollection<DiagnosticItem> _diagnostics = new();

        // ========== 窗口主题切换 ==========
        private SkinType _currentSkin = SkinType.Default;
        private bool _isApplyingSkin;
        private bool _initialized;

        /// <summary>当前选中的代码配色主题（对应控件 CodeTheme 属性，预设由类库 CodeThemePresets 提供）</summary>
        private CodeThemePreset _codeTheme = CodeThemePreset.FollowWindow;

        // ========== 默认 Demo 代码（直接能跑） ==========
        // 只保留实际用到的 using —— 没用到的 using 会触发编译器 Hidden 级诊断 CS8019
        private const string DefaultDemoCode = @"using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 演示宿主类：补全列表顶部描述条会显示这段描述与完整签名。
/// </summary>
public class ScriptHost
{
    /// <summary>演示入口：F6 编译并运行后，输出 Hello 与 Linq 排序结果。</summary>
    public void Main()
    {
        Console.WriteLine(""Hello, CodeForge!"");

        var greeter = new Greeter();
        greeter.Say(""CodeForge"");
        int sum = Add(1, 2);

        var list = new List<string> { ""alpha"", ""beta"", ""gamma"" };
        foreach (var item in list.OrderBy(x => x))
            Console.WriteLine(item);
    }

    /// <summary>把两个整数相加并返回结果。</summary>
    /// <param name=""a"">第一个加数</param>
    /// <param name=""b"">第二个加数</param>
    /// <returns>a 与 b 的和</returns>
    private static int Add(int a, int b) => a + b;
}

public class Greeter
{
    /// <summary>向指定对象输出一句问候语。</summary>
    /// <param name=""who"">要问候的对象名称</param>
    public void Say(string who) => Console.WriteLine($""你好, {who}!"");
}
";

        public MainWindow()
        {
            InitializeComponent();
            ResetWindowChrome(); // WindowStyle=None 下统一 WindowChrome

            // ===== 控件已由 XAML 声明（含主题/字号/鸟瞰图等配置）；这里只订阅事件与初始化内容 =====
            _editor.DiagnosticMessage += (s, msg) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (StatusText != null) StatusText.Text = msg;
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            _editor.CompileRequested += (s, e) => Dispatcher.BeginInvoke(new Action(CompileCode));

            // ===== 初始代码（预热在 Loaded 后执行，确保控件完全初始化）=====
            _editor.Code = DefaultDemoCode;

            // ===== 窗口 Loaded 后确保 Roslyn 预热已启动（构造函数已自动启动，此处是安全网）=====
            this.Loaded += (s, e) => _ = _editor.WarmupRoslynAsync();

            // ===== 错误列表绑定 ObservableCollection =====
            ErrorListBox.ItemsSource = _diagnostics;

            // ===== 标题栏按钮 / 全局快捷键 =====
            BtnMinimize.Click += (_, __) => WindowState = WindowState.Minimized;
            BtnMaxRestore.Click += (_, __) => ToggleMaximize();
            BtnClose.Click += (_, __) => Close();
            StateChanged += (_, __) => UpdateMaxRestoreIcon();

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F5 &&
                    (Keyboard.Modifiers & ModifierKeys.Alt) == 0 &&
                    (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                {
                    e.Handled = true;
                    CompileCode();
                }
                else if (e.Key == Key.F6 &&
                         (Keyboard.Modifiers & ModifierKeys.Alt) == 0 &&
                         (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                {
                    e.Handled = true;
                    RunCompileAndRun_Click();
                }
            };

            Loaded += (s, e) =>
            {
                StatusText.Text = "就绪 · 顶部「引用」按钮=程序集引用管理 / 下方左=错误列表 右=输出 · F5编译 / F6运行 / Ctrl+Space智能提示 / Ctrl+K,T自动排版";
                UpdateMaxRestoreIcon();
                SyncSkinUiSelection(_currentSkin == SkinType.Dark ? "Dark" : "Light");
                // 底部错误列表：初始状态（没有编译，就是 0 错误 / 0 警告）
                RefreshDiagnosticCountText();
            };

            _initialized = true;
            DemoThemeState.Publish(_currentSkin, _codeTheme);   // 初始主题广播（后开的子窗口读现值）
        }

        #region ===== 程序集引用管理弹窗（截图风格：ListBox + 添加 DLL + 移除 + 关闭） =====
        private void BtnReferenceManager_Click(object sender, RoutedEventArgs e)
        {
            var win = new ReferenceManagerDialog(_editor, this)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.ShowDialog();
        }
        #endregion

        #region ===== 错误列表（同步编译结果 + 双击跳转） =====
        private void RefreshDiagnosticCountText()
        {
            int errors = _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            int warnings = _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            if (ErrorListCount != null)
                ErrorListCount.Text = $"（{errors} 错误 / {warnings} 警告）";
        }

        private void ClearDiagnostics()
        {
            _diagnostics.Clear();
            RefreshDiagnosticCountText();
        }

        private void AddDiagnostic(DiagnosticSeverity severity, string message, int line, int column)
        {
            var brush = severity switch
            {
                DiagnosticSeverity.Error => Brushes.IndianRed,
                DiagnosticSeverity.Warning => Brushes.Goldenrod,
                DiagnosticSeverity.Info => Brushes.SteelBlue,
                _ => Brushes.Gray
            };
            string tag = severity switch
            {
                DiagnosticSeverity.Error => "错误",
                DiagnosticSeverity.Warning => "警告",
                DiagnosticSeverity.Info => "信息",
                _ => "隐藏"
            };
            string display = line > 0
                ? $"L{line},C{column}  {message}"
                : message;
            _diagnostics.Add(new DiagnosticItem
            {
                Severity = severity,
                SeverityText = tag,
                SeverityBrush = brush,
                Line = Math.Max(1, line),
                Column = Math.Max(1, column),
                Display = display
            });
            RefreshDiagnosticCountText();
        }

        private void ErrorListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ErrorListBox.SelectedItem is not DiagnosticItem it) return;
                if (_editor.EditorTextBox?.Document == null) return;
                var doc = _editor.EditorTextBox.Document;
                int total = doc.LineCount;
                int line = it.Line < 1 ? 1 : (it.Line > total ? total : it.Line);
                var docLine = doc.GetLineByNumber(line);
                int col = Math.Max(0, it.Column - 1);
                int off = Math.Min(docLine.Offset + col, docLine.EndOffset);
                _editor.EditorTextBox.CaretOffset = off;
                _editor.ScrollToLine(line);
                _editor.EditorTextBox.TextArea.Caret.BringCaretToView();
                _editor.EditorTextBox.Focus();
            }
            catch { /* 容错：跳转失败就忽略 */ }
        }
        #endregion

        #region ===== 编译 / 运行 =====
        private void BtnBuild_Click(object sender, RoutedEventArgs e) => CompileCode();

        private void BtnRun_Click(object sender, RoutedEventArgs e) => RunCompileAndRun_Click();

        /// <summary>打开脚本体编辑器示例：只写方法体（给 Result 赋值），包装代码自动生成并编译运行</summary>
        private void BtnScriptDemo_Click(object sender, RoutedEventArgs e)
        {
            var win = new ScriptDemoWindow { Owner = this };
            win.Show();
        }

        /// <summary>打开 XAML 代码提示演示：元素/属性/取值三级补全（DesignerKit XAML 编辑器预研）</summary>
        private void BtnXamlDemo_Click(object sender, RoutedEventArgs e)
        {
            var win = new XamlDemoWindow { Owner = this };
            win.Show();
        }

        private void BtnClearOutput_Click(object sender, RoutedEventArgs e)
        {
            OutputTextBox.Text = string.Empty;
            StatusText.Text = "输出日志已清空";
        }

        private void AppendOutputLine(string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                OutputTextBox.Text += text + Environment.NewLine;
                OutputTextBox.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>把 Roslyn 返回的 Location 拆成（Line,Column）——1-based</summary>
        private static (int line, int column) GetLineColFromMessage(string rawMessage, Location? location)
        {
            // 优先用 Roslyn 的 Location（如果有）
            if (location != null && location.IsInSource)
            {
                try
                {
                    var span = location.GetLineSpan();
                    return ((int)span.StartLinePosition.Line + 1, (int)span.StartLinePosition.Character + 1);
                }
                catch { /* ignore */ }
            }
            // 回退：从 "XXX.cs(行,列): error XXXXX: Message" 解析
            try
            {
                int lp = rawMessage.IndexOf('(');
                int rp = rawMessage.IndexOf(')', lp + 1);
                if (lp >= 0 && rp > lp + 1)
                {
                    var inner = rawMessage.Substring(lp + 1, rp - lp - 1);
                    var parts = inner.Split(',');
                    if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out var ln) && int.TryParse(parts[1].Trim(), out var cn))
                        return (ln, cn);
                    if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out ln))
                        return (ln, 1);
                }
            }
            catch { /* ignore */ }
            return (0, 0);
        }

        private void CompileCode()
        {
            StatusText.Text = "编译中…";
            ClearDiagnostics();
            AppendOutputLine($"======== 编译开始 {DateTime.Now:HH:mm:ss} ========");
            try
            {
                var compile = _editor.Compile();
                _editor.ShowCompileDiagnostics(compile); // 错误/警告打波浪线；编译成功即清空
                if (compile.Warnings != null && compile.Warnings.Count > 0)
                {
                    foreach (var w in compile.Warnings)
                    {
                        AppendOutputLine($"  [Warning] {w}");
                        var (ln, cn) = GetLineColFromMessage(w, null);
                        AddDiagnostic(DiagnosticSeverity.Warning, w, ln, cn);
                    }
                }
                if (compile.Errors != null && compile.Errors.Count > 0)
                {
                    foreach (var err in compile.Errors)
                    {
                        AppendOutputLine($"  [Error] {err}");
                        var (ln, cn) = GetLineColFromMessage(err, null);
                        AddDiagnostic(DiagnosticSeverity.Error, err, ln, cn);
                    }
                }
                if (compile.Success)
                {
                    AppendOutputLine("  ✅ 编译成功");
                    StatusText.Text = $"编译成功（{compile.AssemblyBytes?.Length ?? 0} bytes，{_diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} 警告）";
                }
                else
                {
                    AppendOutputLine("  ❌ 编译失败");
                    StatusText.Text = $"编译失败（{_diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} 错误，{_diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} 警告）";
                }
            }
            catch (Exception ex)
            {
                AppendOutputLine($"  💥 编译异常：{ex}");
                AddDiagnostic(DiagnosticSeverity.Error, ex.Message, 0, 0);
                StatusText.Text = "编译异常（见输出日志 / 错误列表）";
            }
            AppendOutputLine($"======== 编译结束 {DateTime.Now:HH:mm:ss} ========");
        }

        private void RunCompileAndRun_Click()
        {
            StatusText.Text = "生成并运行中…";
            ClearDiagnostics();
            AppendOutputLine($"======== 生成并运行开始 {DateTime.Now:HH:mm:ss} ========");
            try
            {
                var (compile, run) = _editor.CompileAndRun(typeName: "ScriptHost",methodName: "Main");
                _editor.ShowCompileDiagnostics(compile); // 错误/警告打波浪线；编译成功即清空
                if (compile.Warnings != null && compile.Warnings.Count > 0)
                {
                    foreach (var w in compile.Warnings)
                    {
                        AppendOutputLine($"  [Warning] {w}");
                        var (ln, cn) = GetLineColFromMessage(w, null);
                        AddDiagnostic(DiagnosticSeverity.Warning, w, ln, cn);
                    }
                }
                if (compile.Errors != null && compile.Errors.Count > 0)
                {
                    foreach (var err in compile.Errors)
                    {
                        AppendOutputLine($"  [Error] {err}");
                        var (ln, cn) = GetLineColFromMessage(err, null);
                        AddDiagnostic(DiagnosticSeverity.Error, err, ln, cn);
                    }
                }
                if (!compile.Success)
                {
                    AppendOutputLine("  ❌ 编译失败，中止运行");
                    StatusText.Text = $"编译失败，运行中止（{_diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} 错误）";
                    return;
                }
                AppendOutputLine("  ✅ 编译成功，开始运行…");
                if (run != null)
                {
                    if (!string.IsNullOrEmpty(run.ConsoleOutput))
                        AppendOutputLine(run.ConsoleOutput);
                    if (!string.IsNullOrEmpty(run.ErrorMessage))
                        AppendOutputLine("[ErrorMessage]" + Environment.NewLine + run.ErrorMessage);
                    var elapsed = run.ElapsedMilliseconds;
                    AppendOutputLine($"  🏁 运行结束，Success={run.Success}, Elapsed={elapsed}ms");
                    StatusText.Text = $"运行完成（{elapsed} ms）";
                }
                else
                {
                    AppendOutputLine("  ⚠️  运行结果为空");
                    StatusText.Text = "运行完成，但结果为空";
                }
            }
            catch (Exception ex)
            {
                AppendOutputLine($"  💥 异常：{ex}");
                AddDiagnostic(DiagnosticSeverity.Error, ex.Message, 0, 0);
                StatusText.Text = "运行异常（见输出日志 / 错误列表）";
            }
            AppendOutputLine($"======== 生成并运行结束 {DateTime.Now:HH:mm:ss} ========");
        }
        #endregion

        #region ===== 窗口主题（亮/暗）与代码配色主题（互相独立） =====
        private void TitleBarSkinCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_initialized || _isApplyingSkin) return;
            var tag = (TitleBarSkinCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Light";
            var targetSkin = tag.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? SkinType.Dark : SkinType.Default;
            if (targetSkin == _currentSkin) return;
            ApplyUiSkin(targetSkin);
        }

        /// <summary>窗口 UI 主题：只换 HandyControl 皮肤基调；编辑器代码配色保持用户所选（"跟随窗口"项除外）</summary>
        private void ApplyUiSkin(SkinType targetSkin)
        {
            if (_isApplyingSkin) return;
            _isApplyingSkin = true;
            try { ApplyUiSkinCore(targetSkin); }
            finally { _isApplyingSkin = false; }
        }

        private void ApplyUiSkinCore(SkinType targetSkin)
        {
            // —— 1) 替换 HandyControl Skin 基调字典（仅顶层 MergedDictionaries，禁止递归，防止 StackOverflow） ——
            var skinUri = new Uri(
                targetSkin switch
                {
                    SkinType.Dark => "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml",
                    SkinType.Violet => "pack://application:,,,/HandyControl;component/Themes/SkinViolet.xaml",
                    _ => "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml",
                }, UriKind.Absolute);
            ReplaceSkinAndBumpTheme(Application.Current.Resources, skinUri, bumpTheme: true);
            ReplaceSkinAndBumpTheme(Resources, skinUri, bumpTheme: false);
            ResetWindowChrome();

            // —— 2) 编辑器：显式选择的代码配色不受影响；"跟随窗口"项随窗口明暗走默认配色 ——
            _currentSkin = targetSkin;
            if (_codeTheme == CodeThemePreset.FollowWindow)
                _editor.EditorSkin = targetSkin == SkinType.Dark ? EditorSkinMode.Dark : EditorSkinMode.Light;
            DemoThemeState.Publish(_currentSkin, _codeTheme);   // 子演示窗口跟随

            SyncSkinUiSelection(targetSkin == SkinType.Dark ? "Dark" : "Light");
            StatusText.Text = $"窗口主题：{(targetSkin == SkinType.Dark ? "暗色" : "亮色")} · 代码配色：{GetCurrentCodeThemeName()}";
        }

        private void CodeThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_initialized || _isApplyingSkin) return;
            var key = (CodeThemeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "";
            if (string.IsNullOrEmpty(key)) return;
            if (Enum.TryParse<CodeThemePreset>(key, out var preset))
            {
                if (preset == _codeTheme) return;
                _codeTheme = preset;
                // 走控件公开属性 CodeTheme（枚举）—— 鸟瞰图配色等由控件内部同步
                _editor.CodeTheme = preset;
                DemoThemeState.Publish(_currentSkin, _codeTheme);   // 子演示窗口跟随
                StatusText.Text = $"窗口主题：{(_currentSkin == SkinType.Dark ? "暗色" : "亮色")} · 代码配色：{GetCurrentCodeThemeName()}";
            }
        }

        private string GetCurrentCodeThemeName() =>
            (CodeThemeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? _codeTheme.ToString();


        private static void ReplaceSkinAndBumpTheme(System.Windows.ResourceDictionary root, Uri targetUri, bool bumpTheme = false)
        {
            // 严禁递归：此方法仅处理顶层 MergedDictionaries。
            if (root == null || targetUri == null) return;
            for (int i = root.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var md = root.MergedDictionaries[i];
                if (md.Source == null) continue;
                var src = md.Source.OriginalString ?? "";
                if (src.IndexOf("/HandyControl;component/Themes/Skin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                root.MergedDictionaries.RemoveAt(i);
            }
            var newSkin = new System.Windows.ResourceDictionary { Source = targetUri };
            root.MergedDictionaries.Insert(0, newSkin);

            if (bumpTheme)
            {
                for (int i = root.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var md = root.MergedDictionaries[i];
                    if (md.Source == null) continue;
                    var src = md.Source.OriginalString ?? "";
                    if (src.IndexOf("/HandyControl;component/Themes/Theme", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    root.MergedDictionaries.RemoveAt(i);
                    root.MergedDictionaries.Insert(i, new System.Windows.ResourceDictionary { Source = md.Source });
                    break;
                }
            }
        }

        private void SyncSkinUiSelection(string themeKey)
        {
            var expectedTag = themeKey;
            if (TitleBarSkinCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Tag as string, expectedTag, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var obj in TitleBarSkinCombo.Items)
            {
                if (obj is System.Windows.Controls.ComboBoxItem it &&
                    string.Equals(it.Tag as string, expectedTag, StringComparison.OrdinalIgnoreCase))
                {
                    TitleBarSkinCombo.SelectedItem = it;
                    break;
                }
            }
        }

        #endregion

        #region ===== WindowChrome / 最大化图标 =====
        private void ResetWindowChrome()
        {
            var chrome = new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false,
                GlassFrameThickness = new Thickness(0)
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            UpdateMaxRestoreIcon();
        }

        private void UpdateMaxRestoreIcon()
        {
            var geo = WindowState == WindowState.Maximized
                ? (Geometry)FindResource("Geo.Restore")
                : (Geometry)FindResource("Geo.Maximize");
            if (IconMaxRestore != null) IconMaxRestore.Data = geo;
        }
        #endregion
    }
}
