using Renci.SshNet;
using System.IO;
using System.Text;

namespace SimulatorApp.Slave.Services;

public sealed class GpioHilSshService : IDisposable
{
    private const string RemoteWorkDir = "/tmpgpio";
    private const string RemoteScriptPath = $"{RemoteWorkDir}/install_gpio_hil_mock_v2.sh";
    private const string RemoteSoPath = $"{RemoteWorkDir}/libgpio_hil_mock_v2.so";
    private const string RemoteSourcePath = $"{RemoteWorkDir}/gpio_hil_mock_v2.c";
    private const string RemoteLogPath = $"{RemoteWorkDir}/gpio_hil_mock.log";
    private const string RemoteSimDir = $"{RemoteWorkDir}/di_sim";
    private SshClient? _ssh;
    private ScpClient? _scp;

    public bool IsConnected => _ssh?.IsConnected == true;

    public void Connect(string host, int port, string username, string password)
    {
        Disconnect();

        var auth = new PasswordAuthenticationMethod(username, password);
        var connectionInfo = new ConnectionInfo(host, port, username, auth)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        _ssh = new SshClient(connectionInfo);
        _scp = new ScpClient(connectionInfo);
        _ssh.Connect();
        _scp.Connect();

        if (_ssh is null || !_ssh.IsConnected)
        {
            Disconnect();
            throw new InvalidOperationException("SSH 客户端未连接。");
        }

        if (_scp is null || !_scp.IsConnected)
        {
            Disconnect();
            throw new InvalidOperationException("SCP 客户端未连接。");
        }
    }

    public string RunCommand(string command, TimeSpan? timeout = null)
    {
        if (_ssh is null || !_ssh.IsConnected)
            throw new InvalidOperationException("请先连接 Linux 设备。");

        using var cmd = _ssh.CreateCommand(ToLinuxLineEndings(command));
        cmd.CommandTimeout = timeout ?? TimeSpan.FromSeconds(20);
        var result = cmd.Execute();
        var output = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(result))
            output.AppendLine(result.TrimEnd());

        if (!string.IsNullOrWhiteSpace(cmd.Error))
            output.AppendLine(cmd.Error.TrimEnd());

        if (cmd.ExitStatus != 0)
            throw new InvalidOperationException(output.Length > 0 ? output.ToString() : $"命令执行失败：{command}");

        return output.ToString().TrimEnd();
    }

    public bool RemoteScriptExists()
    {
        var result = RunCommand($"test -f {RemoteScriptPath} && echo yes || echo no");
        return result.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public void UploadInstallerScript()
    {
        if (_scp is null || !_scp.IsConnected)
            throw new InvalidOperationException("请先连接 Linux 设备。");

        var script = ToLinuxLineEndings(BuildInstallerScript());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(script));
        RunCommand($"mkdir -p {RemoteWorkDir}");
        _scp.Upload(stream, RemoteScriptPath);
        RunCommand($"chmod +x {RemoteScriptPath}");
    }

    public void EnsureInstallerScript()
    {
        if (!RemoteScriptExists())
            UploadInstallerScript();
    }

    public void InstallOrRepair(string startupScriptPath)
    {
        var startupScript = NormalizeRemotePath(startupScriptPath);
        if (!RemoteFileExists(startupScript))
            throw new InvalidOperationException($"主程序启动脚本不存在，未进行安装：{startupScript}");

        UploadInstallerScript();
        RunCommand($"sh {RemoteScriptPath} install", TimeSpan.FromSeconds(60));
        RunCommand(BuildEnablePreloadCommand(startupScript), TimeSpan.FromSeconds(30));
    }

    public string ClearScript(string startupScriptPath)
    {
        var startupScript = NormalizeRemotePath(startupScriptPath);
        if (!RemoteFileExists(startupScript))
            throw new InvalidOperationException($"主程序启动脚本不存在，未进行清除：{startupScript}");

        return RunCommand(BuildClearScriptCommand(startupScript), TimeSpan.FromSeconds(30));
    }

    public void SetValue(string position, string value)
    {
        var gpio = NormalizePosition(position);
        var v = NormalizeValue(value);

        RunCommand($"mkdir -p {RemoteSimDir} && printf '{v}\\n' > {RemoteSimDir}/gpio{gpio} && chmod 666 {RemoteSimDir}/gpio{gpio}");
    }

    public Dictionary<string, string> ReadValues(IEnumerable<string> positions)
    {
        var gpios = positions
            .Select(NormalizePosition)
            .Distinct()
            .ToArray();

        if (gpios.Length == 0)
            return new Dictionary<string, string>();

        var joined = string.Join(' ', gpios);
        var output = RunCommand(
            $"mkdir -p {RemoteSimDir}; for g in {joined}; do if [ -f {RemoteSimDir}/gpio$g ]; then v=$(head -n 1 {RemoteSimDir}/gpio$g); else v=; fi; echo \"$g=$v\"; done");

        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());
    }

    public string CheckStatus()
    {
        EnsureInstallerScript();
        return RunCommand(
            "pid=$(pidof ba 2>/dev/null | awk '{print $1}'); " +
            "echo \"ba_pid=${pid:-none}\"; " +
            "if [ -n \"$pid\" ]; then " +
            "tr '\\0' '\\n' < /proc/$pid/environ 2>/dev/null | grep LD_PRELOAD || true; " +
            "grep gpio_hil /proc/$pid/maps 2>/dev/null || true; " +
            "fi; " +
            $"test -f {RemoteScriptPath} && echo script=ok || echo script=missing; " +
            $"test -f {RemoteSoPath} && echo so=ok || echo so=missing");
    }

    public string RestartLinux()
    {
        return RunCommand($"mkdir -p {RemoteWorkDir}; sync; (sleep 1; reboot) >{RemoteWorkDir}/gpio_hil_reboot.log 2>&1 & echo \"Linux 重启命令已下发。\"", TimeSpan.FromSeconds(10));
    }

    public void Disconnect()
    {
        if (_scp is not null)
        {
            if (_scp.IsConnected) _scp.Disconnect();
            _scp.Dispose();
            _scp = null;
        }

        if (_ssh is not null)
        {
            if (_ssh.IsConnected) _ssh.Disconnect();
            _ssh.Dispose();
            _ssh = null;
        }
    }

    public void Dispose() => Disconnect();

    private static string ToLinuxLineEndings(string text)
    {
        var normalized = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    private static string NormalizePosition(string position)
    {
        var text = (position ?? "").Trim();
        if (text.StartsWith("GPIO_", StringComparison.OrdinalIgnoreCase))
            text = text[5..];
        else if (text.StartsWith("gpio", StringComparison.OrdinalIgnoreCase))
            text = text[4..];

        if (!int.TryParse(text, out var gpio) || gpio < 0)
            throw new InvalidOperationException($"位置必须是 GPIO 数字，例如 495。当前值：{position}");

        return gpio.ToString();
    }

    private static string NormalizeValue(string value)
    {
        var text = (value ?? "").Trim();
        return text switch
        {
            "0" => "0",
            "1" => "1",
            _ => throw new InvalidOperationException($"当前值只能写 0 或 1。当前值：{value}")
        };
    }

    private bool RemoteFileExists(string path)
    {
        var result = RunCommand($"test -f {ShellQuote(path)} && echo yes || echo no");
        return result.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRemotePath(string path)
    {
        var text = (path ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("请输入主程序启动脚本路径。");

        if (!text.StartsWith('/'))
            throw new InvalidOperationException($"主程序启动脚本路径必须是 Linux 绝对路径，例如 /home/zlgmcu/lighttpd/sbin/lighttpd_start.sh。当前值：{path}");

        return text;
    }

    private static string ShellQuote(string value)
    {
        return "'" + (value ?? "").Replace("'", "'\"'\"'") + "'";
    }

    private static string BuildClearScriptCommand(string startupScriptPath)
    {
        var startupScriptValue = ShellQuote(NormalizeRemotePath(startupScriptPath));

        return """
               START_SH=__STARTUP_SCRIPT__
               if grep -q 'libgpio_hil_mock_v2.so' "$START_SH" 2>/dev/null; then
                 mkdir -p /tmpgpio
                 TMP_FILE="/tmpgpio/$(basename "$START_SH").gpio_hil_clear.$$"
                 cp -p "$START_SH" "/tmpgpio/$(basename "$START_SH").bak_gpio_hil_clear_$(date +%Y%m%d_%H%M%S)"
                 sed '/libgpio_hil_mock_v2\.so/d' "$START_SH" > "$TMP_FILE"
                 cat "$TMP_FILE" > "$START_SH"
                 rm -f "$TMP_FILE"
                 chmod +x "$START_SH"
               fi
               rm -f /tmpgpio/install_gpio_hil_mock_v2.sh
               rm -f /tmpgpio/libgpio_hil_mock_v2.so
               rm -f /tmpgpio/gpio_hil_mock_v2.c
               rm -f /tmpgpio/gpio_hil_mock.log
               rm -f /tmpgpio/start.sh.gpio_hil
               rm -f /tmpgpio/start.sh.gpio_hil_clear
               rm -rf /tmpgpio/di_sim
               echo "DI仿真脚本已清除。如 BA 已加载 LD_PRELOAD，请重启 Linux 后生效。"
               """.Replace("__STARTUP_SCRIPT__", startupScriptValue);
    }

    private static string BuildEnablePreloadCommand(string startupScriptPath)
    {
        var startupScript = ShellQuote(NormalizeRemotePath(startupScriptPath));
        var preloadLine = ShellQuote($"export LD_PRELOAD={RemoteSoPath}");

        return """
               PRELOAD_LINE=__PRELOAD_LINE__
               START_SH=__STARTUP_SCRIPT__

               if [ ! -f "$START_SH" ]; then
                 echo "未找到 $START_SH，未进行安装。"
                 exit 1
               fi

               mkdir -p /tmpgpio
               TMP_FILE="/tmpgpio/$(basename "$START_SH").gpio_hil.$$"
               cp -p "$START_SH" "/tmpgpio/$(basename "$START_SH").bak_gpio_hil_$(date +%Y%m%d_%H%M%S)"

               if grep -q 'libgpio_hil_mock_v2.so' "$START_SH" 2>/dev/null; then
                 sed "s|.*libgpio_hil_mock_v2\.so.*|$PRELOAD_LINE|" "$START_SH" > "$TMP_FILE"
               elif grep -q '^export MALLOC_CONF=' "$START_SH" 2>/dev/null; then
                 awk -v preload="$PRELOAD_LINE" '{print} /^export MALLOC_CONF=/ {print preload}' "$START_SH" > "$TMP_FILE"
               else
                 {
                   printf '%s\n' "$PRELOAD_LINE"
                   cat "$START_SH"
                 } > "$TMP_FILE"
               fi

               cat "$TMP_FILE" > "$START_SH"
               rm -f "$TMP_FILE"
               chmod +x "$START_SH"
               echo "LD_PRELOAD 已写入 $START_SH，请点击重启Linux后生效。"
               """.Replace("__PRELOAD_LINE__", preloadLine)
                  .Replace("__STARTUP_SCRIPT__", startupScript);
    }

    private static string BuildInstallerScript()
    {
        return """
               #!/bin/sh
               set -eu

               MOCK_DIR="/tmpgpio"
               SIM_DIR="/tmpgpio/di_sim"
               SRC_FILE="$MOCK_DIR/gpio_hil_mock_v2.c"
               SO_FILE="$MOCK_DIR/libgpio_hil_mock_v2.so"
               LOG_FILE="/tmpgpio/gpio_hil_mock.log"

               need_cmd() {
                   if ! command -v "$1" >/dev/null 2>&1; then
                       echo "缺少命令: $1"
                       exit 1
                   fi
               }

               install_mock() {
                   need_cmd gcc
                   mkdir -p "$MOCK_DIR" "$SIM_DIR"
                   : > "$LOG_FILE"
                   chmod 666 "$LOG_FILE"

                   cat > "$SRC_FILE" <<'EOF'
               #define _GNU_SOURCE
               #include <dlfcn.h>
               #include <fcntl.h>
               #include <stdarg.h>
               #include <stdio.h>
               #include <string.h>
               #include <sys/syscall.h>
               #include <unistd.h>

               #ifndef PATH_MAX
               #define PATH_MAX 4096
               #endif

               typedef int (*open_fn)(const char *pathname, int flags, ...);
               typedef int (*openat_fn)(int dirfd, const char *pathname, int flags, ...);

               static void write_log(const char *src, const char *dst)
               {
                   int fd = syscall(SYS_openat, AT_FDCWD, "/tmpgpio/gpio_hil_mock.log",
                                    O_WRONLY | O_CREAT | O_APPEND, 0666);
                   char line[PATH_MAX * 2];
                   int n;
                   if (fd < 0) return;
                   n = snprintf(line, sizeof(line), "pid=%ld redirect %s -> %s\n",
                                (long)getpid(), src, dst);
                   if (n > 0) syscall(SYS_write, fd, line, (size_t)n);
                   syscall(SYS_close, fd);
               }

               static int make_sim_path(const char *path, char *sim_path, size_t sim_path_len)
               {
                   int gpio = -1;
                   char expected[PATH_MAX];
                   char candidate[PATH_MAX];

                   if (!path) return 0;
                   if (sscanf(path, "/sys/class/gpio/gpio%d/value", &gpio) != 1) return 0;
                   if (gpio < 0) return 0;

                   snprintf(expected, sizeof(expected), "/sys/class/gpio/gpio%d/value", gpio);
                   if (strcmp(path, expected) != 0) return 0;

                   snprintf(candidate, sizeof(candidate), "/tmpgpio/di_sim/gpio%d", gpio);
                   if (access(candidate, F_OK) != 0) return 0;

                   snprintf(sim_path, sim_path_len, "%s", candidate);
                   return 1;
               }

               static int open_redirected(const char *pathname, int flags, mode_t mode, int has_mode)
               {
                   static open_fn real_open = NULL;
                   char sim_path[PATH_MAX];
                   const char *target = pathname;
                   if (!real_open) real_open = (open_fn)dlsym(RTLD_NEXT, "open");
                   if (make_sim_path(pathname, sim_path, sizeof(sim_path))) {
                       target = sim_path;
                       write_log(pathname, sim_path);
                   }
                   return has_mode ? real_open(target, flags, mode) : real_open(target, flags);
               }

               int open(const char *pathname, int flags, ...)
               {
                   mode_t mode = 0;
                   if (flags & O_CREAT) {
                       va_list ap;
                       va_start(ap, flags);
                       mode = (mode_t)va_arg(ap, int);
                       va_end(ap);
                       return open_redirected(pathname, flags, mode, 1);
                   }
                   return open_redirected(pathname, flags, mode, 0);
               }

               int open64(const char *pathname, int flags, ...)
               {
                   mode_t mode = 0;
                   if (flags & O_CREAT) {
                       va_list ap;
                       va_start(ap, flags);
                       mode = (mode_t)va_arg(ap, int);
                       va_end(ap);
                       return open_redirected(pathname, flags, mode, 1);
                   }
                   return open_redirected(pathname, flags, mode, 0);
               }

               int __open_2(const char *pathname, int flags) { return open_redirected(pathname, flags, 0, 0); }
               int __open64_2(const char *pathname, int flags) { return open_redirected(pathname, flags, 0, 0); }

               int openat(int dirfd, const char *pathname, int flags, ...)
               {
                   static openat_fn real_openat = NULL;
                   char sim_path[PATH_MAX];
                   const char *target = pathname;
                   mode_t mode = 0;
                   int has_mode = 0;
                   if (!real_openat) real_openat = (openat_fn)dlsym(RTLD_NEXT, "openat");
                   if (flags & O_CREAT) {
                       va_list ap;
                       va_start(ap, flags);
                       mode = (mode_t)va_arg(ap, int);
                       va_end(ap);
                       has_mode = 1;
                   }
                   if (make_sim_path(pathname, sim_path, sizeof(sim_path))) {
                       target = sim_path;
                       dirfd = AT_FDCWD;
                       write_log(pathname, sim_path);
                   }
                   return has_mode ? real_openat(dirfd, target, flags, mode) : real_openat(dirfd, target, flags);
               }

               int openat64(int dirfd, const char *pathname, int flags, ...)
               {
                   static openat_fn real_openat64 = NULL;
                   char sim_path[PATH_MAX];
                   const char *target = pathname;
                   mode_t mode = 0;
                   int has_mode = 0;
                   if (!real_openat64) real_openat64 = (openat_fn)dlsym(RTLD_NEXT, "openat64");
                   if (flags & O_CREAT) {
                       va_list ap;
                       va_start(ap, flags);
                       mode = (mode_t)va_arg(ap, int);
                       va_end(ap);
                       has_mode = 1;
                   }
                   if (make_sim_path(pathname, sim_path, sizeof(sim_path))) {
                       target = sim_path;
                       dirfd = AT_FDCWD;
                       write_log(pathname, sim_path);
                   }
                   return has_mode ? real_openat64(dirfd, target, flags, mode) : real_openat64(dirfd, target, flags);
               }
               EOF

                   gcc -shared -fPIC -ldl -o "$SO_FILE" "$SRC_FILE"
                   chmod 755 "$SO_FILE"
                   echo "安装完成: $SO_FILE"
               }

               case "${1:-install}" in
                   install) install_mock ;;
                   *) echo "用法: sh $0 install"; exit 1 ;;
               esac
               """;
    }
}
