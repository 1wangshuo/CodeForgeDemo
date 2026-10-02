using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Reflection;
using CodeForge;
using HandyControl.Data;
using Microsoft.Win32;

namespace CodeForgeDemo
{
    /// <summary>
    /// 「程序集引用管理」对话框（截图风格：ListBox + 添加 DLL / 移除 / 关闭 三按钮）
    /// </summary>
    public partial class ReferenceManagerDialog
    {
        private readonly CodeEditorControl _editor;
        private readonly Window _owner;

        /// <summary>引用列表显示项（名称 + 版本）——直接绑到 UI</summary>
        private readonly ObservableCollection<ReferenceDisplay> _items = new();

        public ReferenceManagerDialog(CodeEditorControl editor, Window owner)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _owner = owner;
            InitializeComponent();

            // WindowStyle=None 自己画 Chrome：最小化 / 最大化 / 关闭按钮，以及可拖动边框
            ResetWindowChrome();
            BtnClose.Click += (s, e) => Close();

            // 绑定列表
            ReferencesListBox.ItemsSource = _items;
            ReloadFromEngine();
        }

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

        /// <summary>从引擎已有的引用，把它们映射成「名称 + 版本」显示形式</summary>
        private void ReloadFromEngine()
        {
            _items.Clear();
            foreach (var r in _editor.References.References)
            {
                var d = r.Display ?? "";
                var (name, ver) = ParseNameAndVersion(d);
                if (string.IsNullOrWhiteSpace(name)) continue;
                _items.Add(new ReferenceDisplay(name, ver, d));
            }
            // 去重（按 Name 升序，截图里是排好序的）
            var sorted = _items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _items.Clear();
            foreach (var it in sorted) _items.Add(it);
        }

        private static (string name, string version) ParseNameAndVersion(string displayOrPath)
        {
            if (string.IsNullOrWhiteSpace(displayOrPath)) return ("", "");

            // Case A：完整 AssemblyName "System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c..."
            try
            {
                var asmName = new AssemblyName(displayOrPath);
                if (!string.IsNullOrEmpty(asmName.Name))
                    return (asmName.Name, "v" + (asmName.Version?.ToString() ?? "0.0.0.0"));
            }
            catch { /* not an assembly full name */ }

            // Case B：Display 是一个磁盘路径 → 尝试用 AssemblyName.GetAssemblyName
            if (File.Exists(displayOrPath))
            {
                try
                {
                    var asmName = AssemblyName.GetAssemblyName(displayOrPath);
                    if (!string.IsNullOrEmpty(asmName.Name))
                        return (asmName.Name, "v" + (asmName.Version?.ToString() ?? "0.0.0.0"));
                }
                catch { /* ignore */ }
            }

            // Case C：只是一个 DLL 文件名
            string fileName = Path.GetFileName(displayOrPath);
            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string noExt = Path.GetFileNameWithoutExtension(fileName);
                return (noExt, "v?");
            }
            return (displayOrPath, "");
        }

        #region 三个按钮：添加 DLL / 移除 / 关闭
        private void BtnAddDll_Click(object sender, RoutedEventArgs e)
        {
            // —— 初始目录定位到 Sample 根目录下的 References\（用户演示目录，里面有我们准备好的 DemoUserLibrary.dll）
            string refsDir;
            try
            {
                // 策略：先取 Sample 可执行文件所在目录（CodeForgeDemo\bin\Debug\net48\）的「..\..\..\References\」
                // 这样不管是 Debug 还是 Release 都能指回 CodeForgeDemo\References\
                var exePath = typeof(ReferenceManagerDialog).Assembly.Location;
                var exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                refsDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "References"));
                if (!Directory.Exists(refsDir))
                {
                    // 兜底：exe 目录里有没有 References\？（比如发布后用户手动建的）
                    refsDir = Path.Combine(exeDir, "References");
                    if (!Directory.Exists(refsDir)) refsDir = exeDir;
                }
            }
            catch { refsDir = Environment.CurrentDirectory; }

            var dlg = new OpenFileDialog
            {
                Title = "添加引用（选择 .NET 程序集）— 控件会自动归档到程序目录 References\\ 再引用",
                Filter = ".NET 程序集 (*.dll, *.exe)|*.dll;*.exe|所有文件 (*.*)|*.*",
                Multiselect = true,
                InitialDirectory = refsDir
            };
            if (dlg.ShowDialog(_owner) != true) return;

            // —— 归档 + 引用全部由控件 InstallAndReferenceDll 实现（类库职责）；
            //    成功/失败通过 DiagnosticMessage 事件上报给宿主日志/状态栏 ——
            foreach (var path in dlg.FileNames)
            {
                bool ok;
                try { ok = _editor.InstallAndReferenceDll(path); }
                catch (Exception ex) { ok = false;
                    MessageBox.Show(this, $"添加失败:{Path.GetFileName(path)}\n{ex.Message}", "程序集引用管理",
                        MessageBoxButton.OK, MessageBoxImage.Warning); }
                if (!ok)
                {
                    MessageBox.Show(this,
                        $"添加失败:{Path.GetFileName(path)}\n详见状态栏诊断信息（该文件未进入 References\\，不会被引用）。",
                        "程序集引用管理", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            ReloadFromEngine();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (ReferencesListBox.SelectedItem is not ReferenceDisplay sel)
            {
                MessageBox.Show(this, "请先选中列表中要移除的引用", "程序集引用管理", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 找对应的 MetadataReference 并移除：优先 Display 匹配；找不到再按 Display 包含 Name 做软匹配
            var target = _editor.References.References.FirstOrDefault(r => r.Display == sel.OriginalDisplay);
            if (target == null)
            {
                target = _editor.References.References
                    .FirstOrDefault(r => !string.IsNullOrEmpty(r.Display) &&
                                         (r.Display.StartsWith(sel.Name + ",", StringComparison.OrdinalIgnoreCase) ||
                                          r.Display.StartsWith(sel.Name + ".", StringComparison.OrdinalIgnoreCase) ||
                                          r.Display.EndsWith("\\" + sel.Name + ".dll", StringComparison.OrdinalIgnoreCase) ||
                                          r.Display.EndsWith("/" + sel.Name + ".dll", StringComparison.OrdinalIgnoreCase)));
            }
            if (target == null)
            {
                MessageBox.Show(this, $"未在引擎内找到引用：{sel.Name}", "程序集引用管理", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _editor.RemoveReference(target);
            ReloadFromEngine();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) => Close();
        #endregion
    }

    /// <summary>
    /// 引用列表项（截图：左边一列名称，右边是版本号 v4.0.0.0 / v2.0.0.0）
    /// </summary>
    public class ReferenceDisplay
    {
        public string Name { get; }
        public string Version { get; }
        public string OriginalDisplay { get; }

        public ReferenceDisplay(string name, string version, string originalDisplay)
        {
            Name = name;
            Version = version;
            OriginalDisplay = originalDisplay;
        }

        /// <summary>
        /// 直接重写 ToString：ListBox 默认 DisplayMember 就是 ToString → 一行就能显示成 "System.Core v4.0.0.0"
        /// </summary>
        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Version)) return Name ?? "";
            return (Name ?? "") + "  " + Version;
        }
    }
}
