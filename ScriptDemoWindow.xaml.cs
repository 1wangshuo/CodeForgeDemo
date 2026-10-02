using System;
using System.Text;
using System.Windows;
using CodeForge;

namespace CodeForgeDemo
{
    /// <summary>
    /// 脚本体编辑器示例窗口：
    /// - 编辑器里只写方法体（给 Result 赋值 / 加减 / if / for …），包装代码由控件自动生成；
    /// - 「执行」= 控件自动包装 → Roslyn 编译 → 反射调用 Process()，返回值即 Result；
    /// - F5（编辑器内）等价于点「执行」（走 ScriptExecuted 事件）；
    /// - 「查看完整脚本」展示控件自动生成的、与 VisionKit 截图同款的完整代码。
    /// </summary>
    public partial class ScriptDemoWindow : Window
    {
        public ScriptDemoWindow()
        {
            InitializeComponent();

            // 预填一个示例方法体（控件本身默认是空白的；宿主可自行决定是否预填）
            Script.BodyText =
                "// 只写方法体：给 Result 赋值即可（加减 / 判断 / 循环随便写）\n" +
                "Result = 0;\n" +
                "for (int i = 1; i <= 100; i++)\n" +
                "{\n" +
                "    Result = (int)Result + i;\n" +
                "}\n" +
                "Console.WriteLine(\"1+2+...+100 = \" + Result);";

            // 编辑器内 F5 → 控件自动包装 + 编译 + 运行 → 这里接结果
            Script.ScriptExecuted += Script_Executed;

            // 需要引用额外程序集时（例如 VisionKit 的 ScriptMethods 基类）：
            // Script.References.AddReferenceFromFile(@"D:\xxx\VisionKit.Plugin.Script.dll");
            // Script.BaseClassName = "ScriptMethods";
        }

        private void BtnCompile_Click(object sender, RoutedEventArgs e)
        {
            // 只编译（不运行）：控件自动包装方法体 → Roslyn 编译 → 波浪线映射回方法体
            var compile = Script.Compile();
            ShowResult(compile, null);
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            var r = Script.CompileAndRun();
            ShowResult(r.Compile, r.Run);
        }

        private void Script_Executed(object sender, ScriptExecutedEventArgs e)
        {
            ShowResult(e.Compile, e.Run);
        }

        private void BtnShowScript_Click(object sender, RoutedEventArgs e)
        {
            TxtOutput.Text = Script.BuildScript();
            TxtStatus.Text = "以下为自动生成的完整脚本（方法体之外全部由控件包装）";
        }

        private void ShowResult(CodeCompileResult compile, CodeRunResult run)
        {
            var sb = new StringBuilder();

            if (!compile.Success)
            {
                sb.AppendLine("❌ 编译失败：");
                foreach (var err in compile.Errors)
                    sb.AppendLine("  " + err);
                // 波浪线已由控件映射到方法体上（包装代码上的错误不会标进编辑器）
            }
            else if (run == null)
            {
                sb.AppendLine("✅ 编译成功（未运行）");
            }
            else if (run.Success)
            {
                sb.AppendLine($"✅ 执行成功，耗时 {run.ElapsedMilliseconds} ms");
                if (!string.IsNullOrEmpty(run.ConsoleOutput))
                    sb.AppendLine("—— Console 输出 ——").AppendLine(run.ConsoleOutput);
                sb.AppendLine("—— 返回值 (Result) ——").AppendLine(run.ReturnValue == null ? "(null)" : run.ReturnValue.ToString());
            }
            else
            {
                sb.AppendLine("❌ 运行失败：" + run.ErrorMessage);
                if (!string.IsNullOrEmpty(run.ConsoleOutput))
                    sb.AppendLine(run.ConsoleOutput);
            }

            TxtOutput.Text = sb.ToString();
            TxtStatus.Text = !compile.Success ? "编译失败"
                : run == null ? "编译成功（未运行）"
                : run.Success ? $"执行成功（{run.ElapsedMilliseconds} ms）"
                : "运行失败";
        }
    }
}
