using CodeForge;
using System;
using System.Windows;

namespace CodeForgeDemo
{
    /// <summary>
    /// XAML 代码提示演示窗口：宿主就是 C# 演示同款 CodeEditorControl——
    /// HandyControl 主题、Ctrl+滚轮缩放、折叠/行号、按键即弹的补全管线全部复用；
    /// 本窗口只做两件事：SetSyntaxHighlighting("XML") + ExternalCompletionProvider 词表接管。
    /// 为 DesignerKit 的 XAML 表单设计器预研——正式版把静态词表换成程序集反射 + xmlns 前缀解析即可。
    /// </summary>
    public partial class XamlDemoWindow : Window
    {
        private const string Sample = @"<Window x:Class=""Demo.MainWindow""
        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        xmlns:hc=""https://handyorg.github.io/handycontrol""
        Title=""演示"" Height=""450"" Width=""800"">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""*""/>
        </Grid.RowDefinitions>

        <!-- 试试：新行敲 < ；<Grid 后敲空格；Grid. 出附加属性；Height= 直接出取值 -->
        <!-- hc: 前缀提示 = 文档里声明了 xmlns:hc 才有（删掉上面那行再敲 <hc: 就不弹了） -->
        <StackPanel Grid.Row=""0"" Orientation=""Horizontal"" Margin=""10"">
            <TextBlock Text=""姓名："" VerticalAlignment=""Center""/>
            <TextBox Width=""200"" Height=""28"" />
            <Button Content=""确定"" Width=""80"" Height=""28"" Margin=""8,0,0,0"" />
            <hc:Rate Width=""120"" />
        </StackPanel>

        <ListBox Grid.Row=""1"" Margin=""10"" />
    </Grid>
</Window>";

        public XamlDemoWindow()
        {
            InitializeComponent();
            Editor.SetSyntaxHighlighting("XML");
            Editor.ExternalCompletionProvider = XamlCompletionProvider.GetForEditor;
            Editor.Code = Sample;

            ApplyThemeState();                          // 打开时对齐当前主题
            DemoThemeState.Changed += OnThemeChanged;   // 主窗口切主题实时跟随
        }

        private void OnThemeChanged() => ApplyThemeState();

        /// <summary>跟随主窗口：代码配色"跟随窗口"→ 随明暗切 EditorSkin；显式配色 → 直接套用。</summary>
        private void ApplyThemeState()
        {
            if (DemoThemeState.CodeTheme == CodeThemePreset.FollowWindow)
                Editor.EditorSkin = DemoThemeState.Skin == HandyControl.Data.SkinType.Dark
                    ? EditorSkinMode.Dark
                    : EditorSkinMode.Light;
            else
                Editor.CodeTheme = DemoThemeState.CodeTheme;
        }

        protected override void OnClosed(EventArgs e)
        {
            DemoThemeState.Changed -= OnThemeChanged;
            base.OnClosed(e);
        }
    }
}
