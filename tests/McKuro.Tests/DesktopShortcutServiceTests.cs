using System.Diagnostics;
using System.Text;
using McKuro.Core.Services.Infrastructure;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 桌面快捷方式(.lnk)创建。
/// <para>
/// 重要教训:曾用纯托管手写 Shell Link 二进制,单测(只断言字节结构)全绿,
/// 但 Windows 实际读到的 TargetPath 为空、双击无法启动 —— 因为合法解析依赖
/// LinkTargetIDList。因此这里的验收标准是「.lnk 能被 Windows 解析出正确的 TargetPath」,
/// 而不是「字节长得像 .lnk」。
/// </para>
/// </summary>
public class DesktopShortcutServiceTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "McKuro-lnk-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildScript_Sets_Target_And_Optional_Fields()
    {
        var script = DesktopShortcutService.BuildScript(
            @"C:\Users\me\Desktop\鸣潮.lnk",
            @"C:\Games\McKuro\McKuro.exe",
            @"C:\Games\McKuro",
            "-foo",
            @"C:\Games\McKuro\McKuro.exe",
            "鸣潮启动器");

        Assert.Contains("CreateShortcut('C:\\Users\\me\\Desktop\\鸣潮.lnk')", script, StringComparison.Ordinal);
        Assert.Contains("$s.TargetPath='C:\\Games\\McKuro\\McKuro.exe'", script, StringComparison.Ordinal);
        Assert.Contains("$s.WorkingDirectory='C:\\Games\\McKuro'", script, StringComparison.Ordinal);
        Assert.Contains("$s.Arguments='-foo'", script, StringComparison.Ordinal);
        Assert.Contains("$s.IconLocation='C:\\Games\\McKuro\\McKuro.exe'", script, StringComparison.Ordinal);
        Assert.Contains("$s.Description='鸣潮启动器'", script, StringComparison.Ordinal);
        Assert.Contains("$s.Save()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_Omits_Empty_Optional_Fields()
    {
        var script = DesktopShortcutService.BuildScript(@"C:\a.lnk", @"C:\b.exe", null, null, null, null);

        Assert.DoesNotContain("WorkingDirectory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IconLocation", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Description", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_Escapes_Single_Quotes_In_Paths()
    {
        // PowerShell 单引号串中 ' 需翻倍,否则路径含引号时脚本会语法错误(甚至被注入)
        var script = DesktopShortcutService.BuildScript(@"C:\it's\a.lnk", @"C:\it's\b.exe", null, null, null, null);

        Assert.Contains(@"'C:\it''s\a.lnk'", script, StringComparison.Ordinal);
        Assert.Contains(@"'C:\it''s\b.exe'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_Does_Not_Interpolate_DollarSign()
    {
        // 单引号串不插值:含 $ 的路径必须原样传入
        var script = DesktopShortcutService.BuildScript(@"C:\$x\a.lnk", @"C:\$x\b.exe", null, null, null, null);

        Assert.Contains(@"'C:\$x\a.lnk'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateShortcut_Returns_Null_For_Invalid_Input()
    {
        Assert.Null(DesktopShortcutService.CreateShortcut("", @"C:\a.exe"));
        Assert.Null(DesktopShortcutService.CreateShortcut(@"C:\x.lnk", ""));
    }

    /// <summary>
    /// 真实验收:生成的 .lnk 必须能被 Windows 解析出正确 TargetPath(而非仅"文件存在")。
    /// </summary>
    [Fact]
    public void CreateShortcut_Produces_Resolvable_Target()
    {
        if (!DesktopShortcutService.IsSupported)
        {
            return; // 非 Windows 平台不适用
        }

        var dir = TempDir();
        var lnk = Path.Combine(dir, "test.lnk");
        var target = @"C:\Windows\System32\notepad.exe";
        try
        {
            var created = DesktopShortcutService.CreateShortcut(
                lnk, target, workingDir: @"C:\Windows\System32", arguments: "-krqlv=hd", description: "McKuro Test");

            Assert.Equal(lnk, created);
            Assert.True(File.Exists(lnk));

            // 用 Windows 自己的解析器回读:TargetPath 必须正确(这正是手工拼字节时失败的点)
            var readBack = ReadTargetPath(lnk);
            Assert.Equal(target, readBack);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>用 WScript.Shell 回读 .lnk 的 TargetPath(独立于被测实现)。</summary>
    private static string ReadTargetPath(string lnkPath)
    {
        var script =
            "$s=(New-Object -ComObject WScript.Shell).CreateShortcut(" +
            "'" + lnkPath.Replace("'", "''") + "');[Console]::Out.Write($s.TargetPath)";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30_000);
        return output.Trim();
    }
}
