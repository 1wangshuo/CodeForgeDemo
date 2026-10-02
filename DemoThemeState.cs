using CodeForge;
using HandyControl.Data;
using System;

namespace CodeForgeDemo
{
    /// <summary>
    /// 演示窗口主题广播：MainWindow 换主题/代码配色时发布，各子演示窗口订阅并同步自己的编辑器。
    /// （App 级 HC 资源字典 DynamicResource 自动跟随；这里只补 CodeEditorControl 的
    /// EditorSkin / CodeTheme —— 那是控件显式属性，不走资源解析。）
    /// </summary>
    internal static class DemoThemeState
    {
        public static SkinType Skin { get; private set; } = SkinType.Default;
        public static CodeThemePreset CodeTheme { get; private set; } = CodeThemePreset.FollowWindow;

        public static event Action? Changed;

        public static void Publish(SkinType skin, CodeThemePreset codeTheme)
        {
            Skin = skin;
            CodeTheme = codeTheme;
            Changed?.Invoke();
        }
    }
}
