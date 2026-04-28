using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SimulatorApp.Shared.Services;

/// <summary>
/// 内嵌 HTTP REST API（HttpListener），供外部系统读写寄存器
///
/// 端点：
///   GET  /api/registers?address=7296&amp;count=10       读取寄存器（返回 JSON 数组）
///   PUT  /api/registers?address=7296&amp;value=1234     写单个寄存器（FC06 语义）
///   GET  /api/health                                   返回 {"status":"ok"}
/// </summary>
public class RestApiService : IAsyncDisposable
{
    private readonly RegisterBank       _bank;
    private readonly HttpListener       _listener = new();
    private CancellationTokenSource?    _cts;
    private Task?                       _listenTask;

    public bool IsRunning { get; private set; }
    public int  Port      { get; private set; }

    public RestApiService(RegisterBank bank) => _bank = bank;

    // ----------------------------------------------------------------
    // 启动
    // ----------------------------------------------------------------

    public Task StartAsync(int port = 8765)
    {
        if (IsRunning) return Task.CompletedTask;
        Port = port;

        _listener.Prefixes.Clear();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        IsRunning = true;
        _cts      = new CancellationTokenSource();
        _listenTask = AcceptLoopAsync(_cts.Token);

        AppLogger.Info($"REST API 已启动 → http://localhost:{port}/");
        return Task.CompletedTask;
    }

    // ----------------------------------------------------------------
    // 停止
    // ----------------------------------------------------------------

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _listener.Stop();
        if (_listenTask != null)
            await _listenTask.ConfigureAwait(false);
        IsRunning = false;
        AppLogger.Info("REST API 已停止");
    }

    // ----------------------------------------------------------------
    // 请求接收循环
    // ----------------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = HandleAsync(ctx);
        }
    }

    // ----------------------------------------------------------------
    // 请求处理
    // ----------------------------------------------------------------

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;
        resp.ContentType = "application/json; charset=utf-8";

        try
        {
            var path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";

            // GET /api/health
            if (path == "/api/health" && req.HttpMethod == "GET")
            {
                await WriteJsonAsync(resp, new { status = "ok", timestamp = DateTime.Now });
                return;
            }

            // GET /api/registers
            if (path == "/api/registers" && req.HttpMethod == "GET")
            {
                int address = int.Parse(req.QueryString["address"] ?? "0");
                int count   = int.Parse(req.QueryString["count"]   ?? "1");
                count = Math.Clamp(count, 1, 125);

                var values = _bank.ReadRange(address, count);
                await WriteJsonAsync(resp, new { address, count, values });
                return;
            }

            // PUT /api/registers
            if (path == "/api/registers" && req.HttpMethod == "PUT")
            {
                int    address = int.Parse(req.QueryString["address"] ?? "0");
                ushort value   = ushort.Parse(req.QueryString["value"] ?? "0");
                _bank.Write(address, value);
                await WriteJsonAsync(resp, new { success = true, address, value });
                AppLogger.Info($"REST API 写寄存器  addr={address}  value={value}");
                return;
            }

            // 404
            resp.StatusCode = 404;
            await WriteJsonAsync(resp, new { error = "Not Found" });
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            await WriteJsonAsync(resp, new { error = ex.Message });
            AppLogger.Error("REST API 异常", ex);
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse resp, object data)
    {
        var json  = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        resp.OutputStream.Close();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
