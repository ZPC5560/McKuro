using McKuro.Core.Services.Update;

namespace McKuro.Tests;

/// <summary>
/// 自更新延迟替换脚本的文本契约测试。
/// <para>
/// 这些脚本是整个更新链路里**唯一无法调试**的一段:主程序已经退出,脚本在 cmd/bash 里跑,
/// 没有界面也没有日志钩子。拼错一个花括号或颠倒一步顺序,用户看到的现象只是
/// "升级失败" 或 "C 盘越来越小",排障成本极高。因此这里用纯字符串断言把每一处
/// 顺序与清理行为钉死。
/// </para>
/// <para>
/// 回归背景(真实用户反馈):旧版 <c>update.cmd</c> 只 xcopy,不删解压目录(约 500 MB)
/// 与下载的 zip(约 130 MB),每次升级都往 %AppData% 累加。
/// </para>
/// </summary>
public class UpdateLaunchScriptsTests
{
    private const string ExtractDir = @"C:\Users\me\AppData\Roaming\McKuro\updates\1.4.0";
    private const string AppDir = @"D:\Apps\McKuro";
    private const string PackagePath = @"C:\Users\me\AppData\Roaming\McKuro\updates\McKuro-win-x64-1.4.0.zip";
    private const string SelfDelete = @"del /f /q ""%~f0"" >nul 2>&1";

    private static string WindowsReplace(string? packagePath = PackagePath) =>
        UpdateLaunchScripts.BuildWindowsReplaceScript(ExtractDir, AppDir, packagePath);

    /// <summary>把脚本切成"有效命令行"(去掉空行),用于按索引比较先后顺序。</summary>
    private static string[] Lines(string script) =>
        script.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(static l => l.Trim())
            .Where(static l => l.Length > 0)
            .ToArray();

    private static int IndexOfLine(string[] lines, Func<string, bool> predicate)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (predicate(lines[i]))
            {
                return i;
            }
        }
        return -1;
    }

    // ─────────────────────────── Windows 替换脚本:顺序 ───────────────────────────

    [Fact]
    public void WindowsReplace_Starts_With_EchoOff_Then_Chcp()
    {
        var lines = Lines(WindowsReplace());

        Assert.Equal("@echo off", lines[0]);
        Assert.Equal("chcp 65001 >nul", lines[1]);
    }

    /// <summary>
    /// chcp 必须存在:脚本以 UTF-8 写盘,安装目录常含中文,cmd 按当前代码页逐行解析,
    /// 不切 65001 会把多字节路径解析坏。
    /// </summary>
    [Fact]
    public void WindowsReplace_Declares_Utf8_CodePage()
    {
        Assert.Contains("chcp 65001 >nul", WindowsReplace(), StringComparison.Ordinal);
    }

    /// <summary>xcopy 必须带 /e(含空目录)/y(不提示覆盖)/q(静默);缺 /y 会在无人值守时卡住。</summary>
    [Fact]
    public void WindowsReplace_Copies_With_Xcopy_E_Y_Q()
    {
        var script = WindowsReplace();

        Assert.Contains("xcopy /e /y /q", script, StringComparison.Ordinal);
        // 源目录要拼出 "<extract>\*",目标目录要带回末尾分隔符(否则 xcopy 会把目标当文件名)。
        var expectedCopyLine = "xcopy /e /y /q \"" + ExtractDir + "\\*\" \"" + AppDir + "\\\"";
        Assert.Contains(expectedCopyLine, script, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsReplace_Kills_Old_Process_Before_Xcopy()
    {
        var lines = Lines(WindowsReplace());
        var kill = IndexOfLine(lines, static l => l.StartsWith("taskkill /im ", StringComparison.Ordinal));
        var copy = IndexOfLine(lines, static l => l.StartsWith("xcopy ", StringComparison.Ordinal));

        Assert.True(kill >= 0, "缺少 taskkill");
        Assert.True(copy > kill, "xcopy 必须在 taskkill 之后");
        Assert.Contains("taskkill /im McKuro.exe /f", lines[kill], StringComparison.Ordinal);
    }

    /// <summary>
    /// 失败保护的核心:errorlevel 判定必须在**任何删除命令之前**。
    /// 若先删后判,升级失败时用户既没有可用的旧版本文件、也没有重试原料。
    /// </summary>
    [Fact]
    public void WindowsReplace_ErrorLevel_Guard_Precedes_Every_Delete()
    {
        var lines = Lines(WindowsReplace());
        var guard = IndexOfLine(lines, static l => l == "if errorlevel 1 goto keepall");
        var removeDir = IndexOfLine(lines, static l => l.StartsWith("rd /s /q ", StringComparison.Ordinal));
        var removePackage = IndexOfLine(lines, static l => l.StartsWith("del /f /q \"C:\\", StringComparison.Ordinal));
        var selfDelete = IndexOfLine(lines, static l => l == SelfDelete);

        Assert.True(guard > IndexOfLine(lines, static l => l.StartsWith("xcopy ", StringComparison.Ordinal)),
            "errorlevel 判定必须紧跟 xcopy 之后");
        Assert.True(removeDir > guard, "rd 解压目录必须先过 errorlevel 判定");
        Assert.True(removePackage > guard, "del 安装包必须先过 errorlevel 判定");
        Assert.True(selfDelete > guard, "自删行也必须在判定之后(它由 goto 分流进入)");
    }

    /// <summary>
    /// 用阈值 1 而不是 2/4:xcopy 退出码 1 = "没有文件被复制"(解压目录空/路径拼错),
    /// 这是最常见的故障形态,阈值 2 会把它当成功。
    /// </summary>
    [Fact]
    public void WindowsReplace_Guard_Uses_Errorlevel_One_Not_Higher_Threshold()
    {
        var script = WindowsReplace();

        Assert.Contains("if errorlevel 1 goto keepall", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if errorlevel 2", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if errorlevel 4", script, StringComparison.Ordinal);
    }

    /// <summary>失败分支必须真的落到 keepall 标签上,且保留清理提示。</summary>
    [Fact]
    public void WindowsReplace_Failure_Branch_Has_KeepAll_Label()
    {
        var lines = Lines(WindowsReplace());
        var label = IndexOfLine(lines, static l => l == ":keepall");

        Assert.True(label > 0, "缺少 :keepall 标签");
        Assert.Contains(":cleanup", lines);
    }

    /// <summary>
    /// 先 start 新版本、再删旧文件:删除 500 MB 期间用户已经能看到程序起来;
    /// 反过来则一旦删除失败用户可能连程序都打不开。
    /// </summary>
    [Fact]
    public void WindowsReplace_Starts_New_Version_Before_Deleting_Anything()
    {
        var lines = Lines(WindowsReplace());
        var start = IndexOfLine(lines, static l => l.StartsWith("start \"\" ", StringComparison.Ordinal));
        var removeDir = IndexOfLine(lines, static l => l.StartsWith("rd /s /q ", StringComparison.Ordinal));
        var removePackage = IndexOfLine(lines, static l => l.StartsWith("del /f /q \"C:\\", StringComparison.Ordinal));

        Assert.True(start > 0, "缺少 start 重启行");
        Assert.Contains(AppDir + "\\McKuro.exe", lines[start], StringComparison.Ordinal);
        Assert.True(removeDir > start, "rd 解压目录必须在 start 之后");
        Assert.True(removePackage > start, "del 安装包必须在 start 之后");
    }

    [Fact]
    public void WindowsReplace_Removes_Extract_Dir_And_Package()
    {
        var script = WindowsReplace();

        Assert.Contains("rd /s /q \"" + ExtractDir + "\"", script, StringComparison.Ordinal);
        Assert.Contains("del /f /q \"" + PackagePath + "\"", script, StringComparison.Ordinal);
    }

    /// <summary>没有下载包(在线直装/安装器链路)时,不应生成一行指向空路径的 del。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WindowsReplace_Omits_Package_Delete_When_Package_Missing(string? packagePath)
    {
        var lines = Lines(WindowsReplace(packagePath));

        Assert.DoesNotContain(lines, static l => l.StartsWith("del /f /q \"C:", StringComparison.Ordinal));
        // 解压目录仍然要删 —— 它占了 500 MB 里的大头。
        Assert.Contains(lines, static l => l.StartsWith("rd /s /q ", StringComparison.Ordinal));
    }

    /// <summary>自删必须是最后一条有效命令,否则脚本会把自己的后续行一起丢掉。</summary>
    [Fact]
    public void WindowsReplace_SelfDeletes_As_Last_Command()
    {
        var lines = Lines(WindowsReplace());

        Assert.Equal(SelfDelete, lines[^1]);
        Assert.Equal(1, lines.Count(static l => l == SelfDelete));
    }

    [Fact]
    public void WindowsReplace_No_Package_SelfDeletes_As_Last_Command()
    {
        var lines = Lines(WindowsReplace(null));

        Assert.Equal(SelfDelete, lines[^1]);
    }

    /// <summary>
    /// 单 $ 原始字符串的回归锁:写成 $$ 会把 {extractDir} 变成字面量,xcopy 永远找不到源目录
    /// (本仓库真实踩过)。插值后脚本里不该再残留任何花括号。
    /// </summary>
    [Fact]
    public void WindowsReplace_Has_No_Uninterpolated_Braces()
    {
        var script = WindowsReplace();

        Assert.DoesNotContain('{', script);
        Assert.DoesNotContain('}', script);
    }

    [Fact]
    public void WindowsReplace_Uses_Crlf_Line_Endings()
    {
        var script = WindowsReplace();

        Assert.Contains("\r\n", script, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', script.Replace("\r\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsReplace_Ends_With_NewLine()
    {
        Assert.EndsWith("\r\n", WindowsReplace(), StringComparison.Ordinal);
    }

    /// <summary>末尾多余的反斜杠会拼出 <c>"…\1.4.0\\*"</c>,xcopy 对双分隔符的行为依版本而异。</summary>
    [Fact]
    public void WindowsReplace_Normalizes_Trailing_Separators()
    {
        var script = UpdateLaunchScripts.BuildWindowsReplaceScript(ExtractDir + @"\", AppDir + @"\", PackagePath);

        Assert.Contains("\"" + ExtractDir + "\\*\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\*", script, StringComparison.Ordinal);
        Assert.Contains(AppDir + "\\McKuro.exe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsReplace_Honors_Custom_ExeName()
    {
        var script = UpdateLaunchScripts.BuildWindowsReplaceScript(ExtractDir, AppDir, null, "Other.exe");

        Assert.Contains("taskkill /im Other.exe /f", script, StringComparison.Ordinal);
        Assert.Contains(AppDir + "\\Other.exe", script, StringComparison.Ordinal);
    }

    // ─────────────────────────── Windows relaunch 脚本 ───────────────────────────

    [Fact]
    public void Relaunch_Waits_On_Installer_Pid_Then_Starts_New_Version()
    {
        var lines = Lines(UpdateLaunchScripts.BuildWindowsRelaunchScript(4321, AppDir, @"C:\u\setup.exe"));
        var label = IndexOfLine(lines, static l => l == ":wait");
        var poll = IndexOfLine(lines, static l => l.StartsWith("tasklist /nh /fi ", StringComparison.Ordinal));
        var start = IndexOfLine(lines, static l => l.StartsWith("start \"\" ", StringComparison.Ordinal));
        var removeSetup = IndexOfLine(lines, static l => l.StartsWith("del /f /q ", StringComparison.Ordinal));
        var selfDelete = IndexOfLine(lines, static l => l == @"del ""%~f0""");

        Assert.Equal("@echo off", lines[0]);
        Assert.Equal("chcp 65001 >nul", lines[1]);
        Assert.True(label > 0, "缺少 :wait 标签");
        Assert.True(poll > label, "轮询行必须在 :wait 之后");
        Assert.Contains("\"PID eq 4321\"", lines[poll], StringComparison.Ordinal);
        Assert.Contains("&& goto wait", lines[poll], StringComparison.Ordinal);
        Assert.True(start > poll, "安装器退出后才会 start 新版本");
        Assert.True(removeSetup > start, "删 setup 必须在启动新版本之后");
        Assert.True(selfDelete > removeSetup, "自删必须最后");
        Assert.Equal(@"del ""%~f0""", lines[^1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Relaunch_Omits_Setup_Delete_When_Setup_Missing(string? setupPath)
    {
        var lines = Lines(UpdateLaunchScripts.BuildWindowsRelaunchScript(4321, AppDir, setupPath));

        Assert.DoesNotContain(lines, static l => l.StartsWith("del /f /q ", StringComparison.Ordinal));
        Assert.Equal(@"del ""%~f0""", lines[^1]);
    }

    [Fact]
    public void Relaunch_Has_No_Uninterpolated_Braces()
    {
        var script = UpdateLaunchScripts.BuildWindowsRelaunchScript(4321, AppDir, @"C:\u\setup.exe");

        Assert.DoesNotContain('{', script);
        Assert.DoesNotContain('}', script);
    }

    // ─────────────────────────── POSIX 替换脚本 ───────────────────────────

    private static string Posix(
        string? packagePath = "/tmp/updates/p.zip",
        string exeName = "McKuro",
        string appDir = "/Applications/McKuro.app/Contents/MacOS") =>
        UpdateLaunchScripts.BuildPosixReplaceScript(
            "/tmp/updates/1.4.0", appDir, exeName, packagePath, "/tmp/updates/update.log", 987);

    [Fact]
    public void Posix_Starts_With_Bash_Shebang_And_Lf_Only()
    {
        var script = Posix();
        var lines = Lines(script);

        Assert.Equal("#!/bin/bash", lines[0]);
        // shebang 若带 CR 会变成 "bad interpreter: /bin/bash\r" —— 只在目标平台上才炸。
        Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Posix_Redirects_Output_To_Log_With_Posix_Syntax()
    {
        var script = Posix();

        Assert.Contains("exec >>\"/tmp/updates/update.log\" 2>&1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("&>>", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Posix_Kills_Old_Process_Then_Replaces()
    {
        var lines = Lines(Posix());
        var kill = IndexOfLine(lines, static l => l.StartsWith("kill 987 ", StringComparison.Ordinal));
        var forceKill = IndexOfLine(lines, static l => l.StartsWith("kill -9 987 ", StringComparison.Ordinal));
        var copy = IndexOfLine(lines, static l => l.StartsWith("if cp -R ", StringComparison.Ordinal));

        Assert.True(kill > 0, "缺少 kill");
        Assert.True(forceKill > kill, "缺少 kill -9 兜底");
        Assert.True(copy > forceKill, "cp 必须在旧进程退出之后");
    }

    /// <summary>清理必须落在 cp 成功分支里;cp 失败时删目录等于毁掉重试原料。</summary>
    [Fact]
    public void Posix_Removes_ExtractDir_And_Package_Only_After_Successful_Copy()
    {
        var lines = Lines(Posix());
        var copy = IndexOfLine(lines, static l => l.StartsWith("if cp -R ", StringComparison.Ordinal));
        var removeDir = IndexOfLine(lines, static l => l == "rm -rf -- \"/tmp/updates/1.4.0\"");
        var removePackage = IndexOfLine(lines, static l => l == "rm -f -- \"/tmp/updates/p.zip\"");
        var elseLine = IndexOfLine(lines, static l => l == "else");
        var fi = IndexOfLine(lines, static l => l == "fi");

        Assert.True(copy > 0, "缺少 cp -R");
        Assert.True(removeDir > copy, "rm -rf 解压目录必须在 cp 之后");
        Assert.True(removePackage > copy, "rm -f 安装包必须在 cp 之后");
        Assert.True(removeDir < elseLine, "rm -rf 必须仍在 cp 成功分支内");
        Assert.True(removePackage < elseLine, "rm -f 必须仍在 cp 成功分支内");
        Assert.True(fi > elseLine, "if 分支必须闭合");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Posix_Omits_Package_Delete_When_Package_Missing(string? packagePath)
    {
        var script = Posix(packagePath);

        Assert.DoesNotContain("rm -f -- \"/tmp/updates/p.zip\"", script, StringComparison.Ordinal);
        // 仍然要删解压目录。
        Assert.Contains("rm -rf -- \"/tmp/updates/1.4.0\"", script, StringComparison.Ordinal);
        // 退化成注释而不是悬空的 rm -f ""(那会变成 "rm -f --" 后跟空参数)。
        Assert.Contains("# 未提供更新包路径", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Posix_Relaunches_And_SelfDeletes_Last()
    {
        var lines = Lines(Posix());
        var nohup = IndexOfLine(lines, static l => l.StartsWith("nohup ", StringComparison.Ordinal));
        var chmod = IndexOfLine(lines, static l => l.StartsWith("chmod +x ", StringComparison.Ordinal));

        Assert.True(chmod > 0, "缺少 chmod +x");
        Assert.True(nohup > chmod, "nohup 拉起必须在 chmod 之后");
        Assert.Contains("/Applications/McKuro.app/Contents/MacOS/McKuro", lines[nohup], StringComparison.Ordinal);
        Assert.Equal("rm -f -- \"$0\"", lines[^1]);
    }

    /// <summary>
    /// macOS 自带 bash 3.2:bash4 专有写法会直接 syntax error,升级脚本静默失败
    /// (本仓库真实踩过 &amp;&gt;&gt;)。
    /// </summary>
    [Fact]
    public void Posix_Avoids_Bash4_Only_Syntax()
    {
        var script = Posix();

        Assert.DoesNotContain("&>>", script, StringComparison.Ordinal);
        Assert.DoesNotContain("|&", script, StringComparison.Ordinal);
        Assert.DoesNotContain("${", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[[", script, StringComparison.Ordinal);
        Assert.DoesNotContain("declare -A", script, StringComparison.Ordinal);
        Assert.DoesNotContain("mapfile", script, StringComparison.Ordinal);
        Assert.DoesNotContain("<<<", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Posix_Has_No_Uninterpolated_Braces()
    {
        var script = Posix();

        Assert.DoesNotContain('{', script);
        Assert.DoesNotContain('}', script);
        Assert.Contains("cp -R \"/tmp/updates/1.4.0/.\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Posix_Normalizes_Trailing_Slash()
    {
        var script = UpdateLaunchScripts.BuildPosixReplaceScript(
            "/tmp/updates/1.4.0/", "/opt/McKuro//", "McKuro", null, "/tmp/update.log", 42);

        Assert.Contains("cp -R \"/tmp/updates/1.4.0/.\" \"/opt/McKuro/\"", script, StringComparison.Ordinal);
        Assert.Contains("nohup \"/opt/McKuro/McKuro\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Posix_Honors_Custom_ExeName()
    {
        var script = Posix(exeName: "mckuro");

        Assert.Contains("chmod +x \"/Applications/McKuro.app/Contents/MacOS/mckuro\"", script, StringComparison.Ordinal);
        Assert.Contains("nohup \"/Applications/McKuro.app/Contents/MacOS/mckuro\"", script, StringComparison.Ordinal);
    }

    // ─────────────────────────── 参数校验 ───────────────────────────

    [Fact]
    public void BuildWindowsReplaceScript_Rejects_Empty_Directories()
    {
        Assert.Throws<ArgumentException>(
            () => UpdateLaunchScripts.BuildWindowsReplaceScript(" ", AppDir, null));
        Assert.Throws<ArgumentException>(
            () => UpdateLaunchScripts.BuildWindowsReplaceScript(ExtractDir, "", null));
    }

    [Fact]
    public void BuildWindowsRelaunchScript_Rejects_Invalid_Pid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UpdateLaunchScripts.BuildWindowsRelaunchScript(0, AppDir, null));
    }

    [Fact]
    public void BuildPosixReplaceScript_Rejects_Invalid_Arguments()
    {
        Assert.Throws<ArgumentException>(
            () => UpdateLaunchScripts.BuildPosixReplaceScript(ExtractDir, AppDir, "McKuro", null, " ", 42));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UpdateLaunchScripts.BuildPosixReplaceScript(ExtractDir, AppDir, "McKuro", null, "/tmp/x.log", 0));
    }
}
