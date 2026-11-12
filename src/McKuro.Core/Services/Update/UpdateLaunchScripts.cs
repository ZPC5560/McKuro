using System.Text;

namespace McKuro.Core.Services.Update;

/// <summary>
/// 自更新"延迟替换"脚本生成器:把原先散落在 App 层(设置页 ViewModel)里内联拼的
/// <c>update.cmd</c> / <c>relaunch.cmd</c> / <c>update.sh</c> 文本收拢到 Core,
/// 并补上"替换成功后立刻删除更新包"这一步。
/// <para>
/// 为什么必须在这里收口:脚本是唯一在**主程序已经退出之后**还在跑的逻辑,它没有任何界面、
/// 没有任何日志入口,一旦拼错(路径没插值、顺序颠倒)用户只会看到"升级失败/磁盘越来越小",
/// 既没法从 UI 复现也没法加断点。抽到 Core 后可以用纯字符串单测锁定顺序与清理行为。
/// </para>
/// <para>
/// 为什么必须删包(真实用户反馈):zip 绿色包解压到
/// <c>%AppData%\McKuro\updates\&lt;版本&gt;</c>,旧脚本 xcopy 完就结束,
/// 解压目录(约 500 MB)与下载的 zip(约 130 MB)永久留在 C 盘,每次升级累加。
/// </para>
/// <para>
/// 为什么删除顺序不能改:<b>只有 xcopy 成功之后才能删</b>。xcopy 失败(文件被占用、权限不足、
/// 磁盘满)时若先删了更新包,用户连重试的原料都没有,只能重新下载 130 MB。所以脚本里
/// xcopy 的下一行立刻判 errorlevel,失败走 <c>goto keepall</c>,只自删脚本、原样保留解压目录与安装包。
/// </para>
/// <para>
/// 为什么是 <c>if errorlevel 1</c> 而不是 <c>if errorlevel 2</c>/<c>4</c>:xcopy 的退出码是
/// 0=成功、1=没有文件被复制、2=用户按 Ctrl+C 中断、4=初始化错误(内存/磁盘/语法)、5=磁盘写入错误。
/// 阈值写成 2 或 4 会漏掉 1 和 2 —— 而 1 恰恰是"解压目录空了/路径拼错"这种最常见故障。
/// </para>
/// <para>
/// <b>已知残余缺口(实测确认,需产品决策)</b>:xcopy 在"源目录存在但里面没有任何文件"时
/// 只打印 <c>0 File(s) copied</c>,退出码却是 <b>0</b>,因此本判定<b>不会</b>触发 keepall,
/// 脚本照常删掉解压目录与安装包。实测对照:源目录整体不存在 → 退出码 4(判定生效);
/// 源目录为空 → 退出码 0(判定不生效)。要堵这个洞得在 xcopy 后额外解析输出或先数文件数,
/// 但那会让脚本更脆(受语言/代码页影响),当前按契约维持 errorlevel 判据,缺口记录在案。
/// </para>
/// <para>
/// 本类只<b>返回字符串</b>:不写盘(编码由 App 层决定,Windows 脚本须以 UTF-8 写盘并配
/// <c>chcp 65001</c>)、不调用 <see cref="System.Diagnostics.Process.Start(string)"/>
/// (拉起进程是 App 层的职责),因此完全可单测、天然 NativeAOT 安全(无反射、无新依赖)。
/// </para>
/// </summary>
public static class UpdateLaunchScripts
{
    /// <summary>默认可执行文件名(zip 绿色包内的入口)。</summary>
    private const string DefaultExeName = "McKuro.exe";

    /// <summary>
    /// 生成 Windows zip 绿色包的"延迟替换"脚本(update.cmd):等主程序退出 → xcopy 覆盖安装目录
    /// → 重启 → 删除解压目录与安装包 → 自删。
    /// <para>
    /// 顺序即契约,不可重排:<c>@echo off</c> → <c>chcp 65001</c> → 等 2 秒 → taskkill →
    /// 等 1 秒 → <c>xcopy /e /y /q</c> → errorlevel 判定 → <c>start</c> 新版本 →
    /// <c>rd /s /q</c> 解压目录 → <c>del</c> 安装包 → <c>del "%~f0"</c>。
    /// </para>
    /// <para>
    /// 先 <c>start</c> 再删:旧版本已经被 taskkill 干掉,先让用户看到新版本起来,再花时间删
    /// 500 MB;删失败最多留垃圾,不会让用户"没程序可用"。
    /// </para>
    /// <para>
    /// 注意:拼接一律使用<b>单 <c>$</c></b> 的原始字符串(<c>$"""…"""</c>),单花括号即插值。
    /// 写成 <c>$$"""…"""</c> 会把 <c>{extractDir}</c> 变成字面量,xcopy 永远找不到源目录
    /// (本仓库真实踩过)。
    /// </para>
    /// </summary>
    /// <param name="extractDir">解压目录(如 <c>%AppData%\McKuro\updates\1.4.0</c>)。</param>
    /// <param name="appDir">当前安装目录(被覆盖的目标)。</param>
    /// <param name="packagePath">下载的 zip 完整路径;为 <c>null</c>/空白时不生成删除安装包的行。</param>
    /// <param name="exeName">重启的入口文件名,默认 <c>McKuro.exe</c>。</param>
    public static string BuildWindowsReplaceScript(
        string extractDir,
        string appDir,
        string? packagePath,
        string exeName = DefaultExeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDir);

        var extract = NormalizeWindowsDirectory(extractDir);
        var app = NormalizeWindowsDirectory(appDir);
        var exe = ResolveExeName(exeName);
        var exePath = $"{app}\\{exe}";

        // 安装包删除行按需生成:在线直装/安装器链路没有下载包,写死一行 del 只会多一条报错。
        // 失败提示也随之一致 —— 没有安装包时写"keep the update package"会误导排障。
        var hasPackage = !string.IsNullOrWhiteSpace(packagePath);
        var removePackageLine = hasPackage
            ? $"del /f /q \"{packagePath!.Trim()}\" >nul 2>&1\n"
            : string.Empty;
        var keepMessage = hasPackage
            ? "echo update replace failed, keeping update files and package for retry"
            : "echo update replace failed, keeping update files for retry";

        var script = $"""
            @echo off
            chcp 65001 >nul
            timeout /t 2 /nobreak >nul
            taskkill /im {exe} /f >nul 2>&1
            timeout /t 1 /nobreak >nul
            xcopy /e /y /q "{extract}\*" "{app}\"
            if errorlevel 1 goto keepall
            start "" "{exePath}"
            rd /s /q "{extract}"
            {removePackageLine}goto cleanup
            :keepall
            rem xcopy failed - keep everything so the update can be retried
            {keepMessage}
            :cleanup
            del /f /q "%~f0" >nul 2>&1
            """;

        return Finish(script, WindowsNewLine);
    }

    /// <summary>
    /// 生成安装器(Inno setup)链路的"等待安装结束再拉起"脚本(relaunch.cmd):
    /// 轮询安装器 PID → 安装器退出后启动新版 → 删除下载的 setup → 自删。
    /// <para>
    /// 为什么需要它:Inno 的 <c>RestartApplications</c> 只重启"优雅退出"的应用,
    /// 本应用有关闭隐藏到托盘逻辑,被 Restart Manager 强杀的实例不会被拉起,
    /// 升级后用户会看到"程序没了"。用脚本盯 PID 是唯一与安装器实现无关的兜底。
    /// </para>
    /// <para>
    /// 删除 setup 放在 <c>start</c> 之后:新增的这步清理失败(比如安装器进程还没真正释放文件句柄)
    /// 绝不能影响新版启动,所以放在最后、失败也只当没删掉。
    /// </para>
    /// </summary>
    /// <param name="installerPid">安装器进程 PID。</param>
    /// <param name="appDir">安装目录。</param>
    /// <param name="setupPath">下载的安装器路径;为 <c>null</c>/空白时不生成删除行。</param>
    /// <param name="exeName">重启的入口文件名,默认 <c>McKuro.exe</c>。</param>
    public static string BuildWindowsRelaunchScript(
        int installerPid,
        string appDir,
        string? setupPath,
        string exeName = DefaultExeName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installerPid);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDir);

        var app = NormalizeWindowsDirectory(appDir);
        var exe = ResolveExeName(exeName);
        var exePath = $"{app}\\{exe}";

        var removeSetupLine = string.IsNullOrWhiteSpace(setupPath)
            ? string.Empty
            : $"del /f /q \"{setupPath.Trim()}\" >nul 2>&1\n";

        // chcp:脚本以 UTF-8 写盘,安装目录/下载路径可能含中文;cmd 按当前代码页逐行解析批处理,
        // 不先切 65001 会把 UTF-8 的多字节路径拆坏,start 的参数就成了乱码路径。
        var script = $"""
            @echo off
            chcp 65001 >nul
            :wait
            timeout /t 1 /nobreak >nul
            tasklist /nh /fi "PID eq {installerPid}" | find "{installerPid}" >nul && goto wait
            start "" "{exePath}"
            {removeSetupLine}del "%~f0"
            """;

        return Finish(script, WindowsNewLine);
    }

    /// <summary>
    /// 生成 macOS/Linux 的"延迟替换"脚本(update.sh):等旧进程退出 → <c>cp -R</c> 覆盖
    /// → <c>chmod +x</c> → 清理解压目录与安装包 → <c>nohup</c> 拉起新版 → 自删。
    /// <para>
    /// 为什么只准 POSIX/bash 3.2 语法:macOS 自带的是 bash 3.2(2007 年,GPLv3 之前),
    /// <c>&amp;&gt;&gt;</c>、<c>|&amp;</c>、<c>${var,,}</c>、关联数组等 bash4 写法会直接
    /// "syntax error near unexpected token",升级脚本静默失败 —— 本仓库真实踩过。
    /// 这里统一用 <c>exec &gt;&gt;log 2&gt;&amp;1</c> + <c>&gt;"$log" 2&gt;&amp;1</c> 的 POSIX 写法。
    /// </para>
    /// <para>
    /// 为什么清理放在 <c>if cp … ; then</c> 分支里:cp 失败时删掉解压目录等于毁掉重试原料,
    /// 和 Windows 侧 <c>goto keepall</c> 是同一个约束。
    /// </para>
    /// </summary>
    /// <param name="extractDir">解压目录(不含末尾斜杠也可)。</param>
    /// <param name="appDir">安装目录(被覆盖的目标)。</param>
    /// <param name="exeName">重启的入口文件名(Unix 可执行文件名可能无扩展名)。</param>
    /// <param name="packagePath">下载的更新包路径;为 <c>null</c>/空白时不生成删除行。</param>
    /// <param name="logPath">脚本日志文件路径(脚本内 <c>exec</c> 重定向到它)。</param>
    /// <param name="oldPid">要终止的旧进程 PID。</param>
    public static string BuildPosixReplaceScript(
        string extractDir,
        string appDir,
        string exeName,
        string? packagePath,
        string logPath,
        int oldPid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(oldPid);

        var exe = ResolveExeName(exeName);
        var extract = NormalizePosixDirectory(extractDir);
        var app = NormalizePosixDirectory(appDir);
        var exePath = $"{app}/{exe}";
        var log = logPath.Trim();

        // 与 Windows 侧同一策略:没有下载包时不留悬空的 rm -f,换成一行注释说明为什么不删。
        // 日志文案跟着一起切换 —— 没有安装包时还写"与更新包已清理"会让排障的人以为清理漏了一半。
        var hasPackage = !string.IsNullOrWhiteSpace(packagePath);
        var removePackageLine = hasPackage
            ? $"rm -f -- \"{packagePath!.Trim()}\""
            : "# 未提供更新包路径,跳过 rm -f";
        var doneMessage = hasPackage ? "替换完成,解压目录与更新包已清理" : "替换完成,解压目录已清理";
        var failedMessage = hasPackage ? "cp 失败,保留解压目录与更新包以便重试" : "cp 失败,保留解压目录以便重试";

        // 注意:此处同样是单 $ 原始字符串(单花括号即插值);$$ 会让 {extract} 变成字面量,
        // cp 会把一个名为 "{extract}/." 的路径当成源目录,永远失败。
        var script = $"""
            #!/bin/bash
            exec >>"{log}" 2>&1
            echo "$(date '+%F %T') update.sh 启动:目标进程 {oldPid} → {app}"
            sleep 2
            kill {oldPid} 2>/dev/null
            for i in 1 2 3 4 5; do
                kill -0 {oldPid} 2>/dev/null || break
                sleep 1
            done
            kill -9 {oldPid} 2>/dev/null
            sleep 1
            echo "$(date '+%F %T') 旧进程已退出,开始替换"
            if cp -R "{extract}/." "{app}/" ; then
                chmod +x "{exePath}"
                rm -rf -- "{extract}"
                {removePackageLine}
                echo "$(date '+%F %T') {doneMessage}"
            else
                echo "$(date '+%F %T') {failedMessage}"
            fi
            echo "$(date '+%F %T') 拉起新版本"
            nohup "{exePath}" >>"{log}" 2>&1 &
            sleep 1
            echo "$(date '+%F %T') 已拉起新版本"
            rm -f -- "$0"
            """;

        return Finish(script, PosixNewLine);
    }

    private const string WindowsNewLine = "\r\n";
    private const string PosixNewLine = "\n";

    /// <summary>空/空白 <paramref name="exeName"/> 回落默认值,避免拼出 <c>"C:\App\"</c> 这种残缺路径。</summary>
    private static string ResolveExeName(string? exeName) =>
        string.IsNullOrWhiteSpace(exeName) ? DefaultExeName : exeName.Trim();

    /// <summary>
    /// 去掉目录末尾多余的分隔符,避免拼出 <c>"C:\App\\*"</c>(xcopy 对双分隔符的处理依版本而异)。
    /// 盘符根 <c>C:\</c> 必须保留末尾分隔符 —— 去掉后 <c>C:</c> 在 cmd 里表示"该盘当前目录",
    /// 会把文件写到意料之外的地方。
    /// </summary>
    private static string NormalizeWindowsDirectory(string dir)
    {
        var trimmed = dir.Trim().TrimEnd('\\', '/');
        return trimmed.EndsWith(':') ? trimmed + '\\' : trimmed;
    }

    /// <summary>POSIX 侧同理;根目录 <c>/</c> 去掉分隔符会变成空串,必须单独保留。</summary>
    private static string NormalizePosixDirectory(string dir)
    {
        var trimmed = dir.Trim().TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    /// <summary>
    /// 统一行尾并补末尾换行。
    /// <para>
    /// 原始字符串的行尾来自 <b>源文件</b>:Windows 检出的 <c>.cs</c> 是 CRLF,
    /// 若原样写进 <c>update.sh</c>,shebang 会变成 <c>#!/bin/bash\r</c>,
    /// 在 macOS 上报 "bad interpreter: No such file or directory"。反向亦然:
    /// cmd 对纯 LF 容错但习惯用 CRLF。因此显式钉住行尾,让脚本产物与构建平台无关。
    /// </para>
    /// </summary>
    private static string Finish(string script, string newLine)
    {
        var normalized = script.ReplaceLineEndings(newLine);
        return normalized.EndsWith(newLine, StringComparison.Ordinal) ? normalized : normalized + newLine;
    }
}
