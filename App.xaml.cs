using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;

namespace CodeForgeDemo
{
    public partial class App : Application
    {
        // ===== 单文件分发：入口最早处注册嵌入依赖解析 =====
        // App 的 XAML（BAML）里使用了 HandyControl 类型，解析发生在任何静态引用之前，
        // 必须赶在 InitializeComponent 之前把 AssemblyResolve 挂上。
        static App()
        {
            CodeForge.CodeForgeRuntime.Initialize();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ===== 基础环境（宿主职责示例）：注册自定义程序集解析 =====
            // 脚本运行期需要绑定用户通过「引用管理器」添加的 DLL，宿主自己决定探测哪些目录。
            RegisterAssemblyProbing();

            // 全局兜底：过滤 HandyControl 换肤期间偶发的 Win32 句柄无效异常
            // 0x80070578 = ERROR_INVALID_WINDOW_HANDLE (在 NonClient/WindowChrome 重算期间窗口句柄临时销毁)
            DispatcherUnhandledException += (s, args) =>
            {
                var ex = args.Exception;
                if (IsWindowChromeRecalcException(ex))
                {
                    args.Handled = true;
                }
            };
        }

        /// <summary>
        /// 宿主端程序集解析注册：脚本运行期解析用户添加的 DLL 时，依次探测
        ///   1) exe 目录下的 References\（引擎已内置该约定）
        ///   2) 开发期源码目录 CodeForgeDemo\References\（从 IDE 直接运行时兜底）
        /// 部署布局不同的宿主可按需增删自己的探测目录。
        /// </summary>
        private static void RegisterAssemblyProbing()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string shortName = new AssemblyName(e.Name).Name + ".dll";

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] probeDirs =
                {
                    Path.Combine(exeDir, "References"),
                    Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "References")),
                };

                foreach (var dir in probeDirs)
                {
                    try
                    {
                        string candidate = Path.Combine(dir, shortName);
                        if (!File.Exists(candidate)) continue;
                        return Assembly.Load(File.ReadAllBytes(candidate));
                    }
                    catch { /* 尝试下一个目录 */ }
                }
                return null;
            };
        }

        private static bool IsWindowChromeRecalcException(Exception ex)
        {
            if (ex == null) return false;

            // 1) 顶层就是 Win32Exception（罕见）
            if (ex is Win32Exception win32)
            {
                // 0x80070578 或 HResult 含此错误码
                if ((uint)win32.NativeErrorCode == 0x80070578 ||
                    (uint)win32.HResult == 0x80070578 ||
                    win32.Message.Contains("0x80070578"))
                    return true;
            }

            // 2) TargetInvocationException -> 内部 Win32Exception（用户遇到的堆栈）
            if (ex is TargetInvocationException tex && tex.InnerException != null)
            {
                if (tex.InnerException is Win32Exception innerWin32)
                {
                    if ((uint)innerWin32.NativeErrorCode == 0x80070578 ||
                        (uint)innerWin32.HResult == 0x80070578 ||
                        innerWin32.Message.Contains("0x80070578"))
                        return true;
                }
                // 再深入一层（极罕见）
                return IsWindowChromeRecalcException(tex.InnerException);
            }

            // 3) 通用：检查 HResult 是否命中 0x80070578 / 0x80131604 (TargetInvocation)
            try
            {
                uint hr = unchecked((uint)ex.HResult);
                if (hr == 0x80070578) return true;
            }
            catch { }

            // 4) 堆栈关键词：HasCustomChrome / GetEffectiveClientRect
            var stack = ex.StackTrace ?? "";
            if (stack.Contains("HwndMouseInputProvider.HasCustomChrome") ||
                stack.Contains("GetEffectiveClientRect"))
                return true;

            return false;
        }
    }
}
