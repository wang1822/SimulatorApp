using Modbus.Data;
using Modbus.Device;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using System.Net;
using System.Net.Sockets;
using ProtocolType = SimulatorApp.Shared.Models.ProtocolType;

namespace SimulatorApp.Slave.Services;

// ============================================================================
// TcpSlaveService — Modbus TCP 从站服务
// ============================================================================
//
// 【你在做什么？】
//   这是一个"从站模拟器"的核心服务。它在本机启动一个 TCP 服务器，
//   假装自己是真实的 Modbus 设备（比如一台 PLC、电表、传感器），
//   响应来自 EMS（能源管理系统）或 SCADA 等上位机的轮询请求。
//
// 【Modbus TCP 协议简介】
//   Modbus 是一种工业通信协议，用于设备之间交换数据。
//   "从站"就是被动响应的那一方——上位机（主站）问什么，从站答什么。
//   TCP 版本就是在普通 TCP 连接上跑 Modbus 协议，默认端口 502。
//
//   Modbus 的数据模型分为 4 个区：
//   ├─ Coil（线圈）           ：可读写的布尔量，功能码 01（读）/ 05（写）
//   ├─ Input Discrete（离散输入）：只读的布尔量，功能码 02（读）
//   ├─ Input Register（输入寄存器）：只读的 16 位整数，功能码 04（读）
//   └─ Holding Register（保持寄存器）：可读写的 16 位整数，功能码 03（读）/ 06/16（写）
//       ↑ 我们主要用这个
//
// 【类实现的接口】
//   - ISlaveService        ：Start/Stop 生命周期管理
//   - IRegisterSnapshotSlaveService：允许外部批量替换寄存器快照值
//     （比如用户切换监听环境时，需要一次性把新设备的寄存器值刷进去）
//
// 【依赖的第三方库】
//   NModbus4 2.1.0：一个开源的 Modbus 协议栈，帮我们处理了协议帧的编解码。
//   我们只需要告诉它"这个地址的值是多少"，它就会自动响应主站的轮询。
//
// 【RegisterBank 是什么？】
//   它是我们 SimulatorApp 自己维护的一个 65536 个 ushort 的数组，
//   充当"数据中转站"。所有设备 ViewModel 把值写到这里，
//   然后这个 Service 再把值同步给 NModbus4 的 DataStore，
//   最终由 NModbus4 响应给 TCP 客户端（EMS/主站）。
// ============================================================================
public class TcpSlaveService : ISlaveService, IRegisterSnapshotSlaveService
{
    // ========================================================================
    // 字段（类的内部状态）
    // ========================================================================

    // RegisterBank：我们自己的寄存器内存池，65536 个寄存器的数组
    private readonly RegisterBank        _bank;

    // TcpListener：.NET 自带的 TCP 监听器，负责接受客户端连接
    // 例如监听 0.0.0.0:502，任何 EMS 连过来都会由它接收
    private TcpListener?                 _tcpListener;

    // ModbusTcpSlave：NModbus4 库的核心对象，帮我们处理 Modbus 协议
    private ModbusTcpSlave?              _slave;

    // CancellationTokenSource：用于优雅停止后台监听线程
    // 调用 Cancel() 后，_cts.Token 的状态会变成"已请求取消"
    private CancellationTokenSource?     _cts;

    // 后台监听任务：Listen() 是同步阻塞的，必须放在 Task.Run 里执行
    private Task?                        _listenTask;

    // DataStore：NModbus4 的数据存储对象，4 个区的数据都在这里面
    // 主站查询时，NModbus4 会直接从 DataStore 里取数据返回
    private DataStore?                   _dataStore;

    // _snapshotAddresses：记录当前快照覆盖了哪些地址
    // 用于 ReplaceSnapshotValues 时清零已失效的旧地址
    private readonly HashSet<int>        _snapshotAddresses = new();

    // 线程同步锁：DataStore 可能在多个线程被访问
    // （UI 线程写 Bank → Bank 事件 → 本 Service 写 DataStore；
    //   TCP 客户端请求 → DataStore 读）
    // 任何访问 _dataStore 的操作都必须 lock 这个对象
    private readonly object              _dataStoreSyncRoot = new();

    // ========================================================================
    // 公共属性（外部通过 ISlaveService 接口使用）
    // ========================================================================

    // 从站是否正在运行（监听中）
    public bool         IsRunning { get; private set; }

    // 从站 ID：Modbus 协议中每个从站有唯一的 1 字节地址（1~247）
    // 主站发请求时会带上目标 SlaveId，只有匹配的从站才响应
    public byte         SlaveId   { get; private set; }

    // 协议类型（这里是 TCP）
    public ProtocolType Protocol  => ProtocolType.Tcp;

    // 监听的 IP 地址（默认 0.0.0.0 表示监听本机所有网卡）
    public string ListenAddress { get; set; } = "0.0.0.0";

    // 监听的端口号（Modbus TCP 标准端口是 502）
    public int    Port          { get; set; } = 502;

    // 功能码：决定响应到 DataStore 的哪个区
    // 3 = Holding Register（默认，最常用）
    // 1 = Coil, 2 = Input Discrete, 4 = Input Register
    public byte   FunctionCode  { get; set; } = 3;

    // 地址过滤器：调用方可以设置这个委托来限制哪些地址可以被外部写入
    // 比如只允许写入某个设备范围内的地址
    public Func<int, bool>? RegisterAddressFilter { get; set; }

    // 绑定的设备标识：用于外部写入时标记是哪个设备被写入了
    public string? BoundDeviceKey { get; set; }

    // 请求事件：每当有主站来读写寄存器时触发
    // 参数：(功能码, 起始地址, 寄存器数量, 来源标识字符串)
    // SlaveViewModel 订阅此事件来统计请求次数和日志
    public event Action<byte, int, int, string>? OnRequest;

    // ========================================================================
    // 构造函数
    // ========================================================================

    /// <summary>
    /// 构造函数：依赖注入框架会把共享的 RegisterBank 传进来。
    /// RegisterBank 是单例（整个应用只有一个实例），
    /// 所以所有 Service 和 ViewModel 共享同一个寄存器池。
    /// </summary>
    public TcpSlaveService(RegisterBank bank)
    {
        _bank = bank;
    }

    // ========================================================================
    // StartAsync — 启动从站监听
    // ========================================================================

    /// <summary>
    /// 启动 TCP 从站服务，开始监听并响应 Modbus TCP 请求。
    ///
    /// 【启动流程】
    ///   1. 创建 NModbus4 的 DataStore（空的寄存器数据区）
    ///   2. 把 RegisterBank 中的当前值同步到 DataStore（SyncBankToDataStore）
    ///   3. 启动 TcpListener 监听指定 IP 和端口
    ///   4. 创建 ModbusTcpSlave 并绑定 DataStore
    ///   5. 订阅 NModbus4 的读写事件（用于日志和外部写入检测）
    ///   6. 订阅 RegisterBank 的写入事件（UI 改了值 → 实时同步给 NModbus4）
    ///   7. 在后台线程调用 Listen()，这是一个同步阻塞方法
    ///      （它会循环接受客户端连接、解析 Modbus 帧、返回响应）
    /// </summary>
    /// <param name="slaveId">从站 ID（1~247）</param>
    /// <param name="cancellationToken">外部取消令牌</param>
    public async Task StartAsync(byte slaveId, CancellationToken cancellationToken = default)
    {
        // 防止重复启动
        if (IsRunning) return;
        SlaveId = slaveId;

        // CreateLinkedTokenSource：把外部传入的 token 和我们自己的 token 关联起来
        // 任何一个被取消，_cts.Token 都会变成"已请求取消"
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // 步骤 1：创建 NModbus4 的数据存储
            // DataStoreFactory.CreateDefaultDataStore() 返回一个包含 4 个区的 DataStore
            _dataStore = DataStoreFactory.CreateDefaultDataStore();

            // 步骤 2：把 RegisterBank 的初始值同步到 DataStore
            // 这一步很重要！如果 RegisterBank 里已经有用户设置的值，必须刷进去
            SyncBankToDataStore();

            // 步骤 3：启动 .NET TCP 监听器
            // TcpListener 会绑定到指定的 IP:端口，准备接收客户端连接
            _tcpListener = new TcpListener(IPAddress.Parse(ListenAddress), Port);
            _tcpListener.Start();

            // 步骤 4：创建 NModbus4 的 TCP 从站
            // ModbusTcpSlave.CreateTcp(...) 内部会管理所有 TCP 客户端连接
            // 不需要我们手动 Accept 客户端——NModbus4 帮我们做了
            _slave = ModbusTcpSlave.CreateTcp(slaveId, _tcpListener);
            _slave.DataStore = _dataStore;

            // 步骤 5：订阅 NModbus4 的读写事件
            // DataStoreReadFrom  ：主站来读寄存器时触发（我们用来记录日志）
            // DataStoreWrittenTo ：主站来写寄存器时触发（我们需要把写入的值同步回 RegisterBank）
            _slave.DataStore.DataStoreReadFrom  += (s, e) => OnDataStoreRead(e);
            _slave.DataStore.DataStoreWrittenTo += (s, e) => OnDataStoreWritten(e);

            // 步骤 6：订阅 RegisterBank 的写入事件
            // 当用户在 UI 上改了寄存器值 → RegisterBank.Write() 被调用
            // → 触发这个事件 → SyncOneRegister 把值同步给 NModbus4 DataStore
            // 这样主站下一次轮询就能读到新值了
            //
            // 注意：NModbus4 的索引是从 1 开始的（Modbus 协议规定），
            // 我们的 RegisterBank 是从 0 开始的，所以 SyncOneRegister 里要 +1
            _bank.OnRegisterWritten += SyncOneRegister;

            AppLogger.Info($"TCP 从站启动：{ListenAddress}:{Port}  SlaveID={slaveId}");
            IsRunning = true;

            // 步骤 7：启动后台监听线程
            // Listen() 是同步阻塞方法——它会一直运行，直到 socket 被关闭或出错
            // 所以必须用 Task.Run 把它放到线程池的后台线程上，
            // 不能直接在 async 方法里调用它（会阻塞 UI 线程）
            //
            // 为什么不传 cancellationToken 给 Task.Run？
            //   因为 Stop 时我们会先 Cancel token，如果 token 已经传给了 Task.Run，
            //   这个 Task 会直接变成 Canceled 状态而不是 RunToCompletion，
            //   我们就没法 await 它正常结束了。
            var token = _cts.Token;
            _listenTask = Task.Run(() =>
            {
                try
                {
                    // Listen() 内部会循环处理客户端请求
                    // 直到 socket 关闭（我们在 StopAsync 中 Dispose _slave）
                    _slave.Listen();
                }
                catch (Exception ex)
                {
                    // 主动 Stop 时，Dispose/Stop 会导致 socket 关闭，
                    // Listen() 抛出异常是正常行为，静默忽略即可。
                    // 只有非预期异常才记录日志。
                    if (!token.IsCancellationRequested)
                        AppLogger.Error("TCP 从站监听异常", ex);
                }
            });

            // 这里直接返回 CompletedTask，因为真正的监听工作在 _listenTask 后台线程上
            // StartAsync 本身不需要等待监听结束
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            AppLogger.Error($"TCP 从站启动失败：{ex.Message}", ex);
            throw; // 重新抛出异常，让上层（SlaveViewModel）知道启动失败了
        }
    }

    // ========================================================================
    // StopAsync — 停止从站监听
    // ========================================================================

    /// <summary>
    /// 优雅停止 TCP 从站服务。
    ///
    /// 【停止流程】
    ///   1. 标记 IsRunning = false（阻止新的请求处理）
    ///   2. 取消订阅 RegisterBank 事件（停止接收 UI 的值更新）
    ///   3. Cancel token（通知后台线程该结束了）
    ///   4. Dispose ModbusTcpSlave（关闭所有客户端连接，停止 Listen）
    ///   5. Stop TcpListener（释放端口）
    ///   6. 等待后台线程结束（最多等 3 秒）
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;

        // 取消订阅：不再从 RegisterBank 同步值到 DataStore
        _bank.OnRegisterWritten -= SyncOneRegister;

        // 发送取消信号给后台线程
        _cts?.Cancel();

        // Dispose slave 会关闭所有 TCP 连接，导致 Listen() 抛出异常并退出
        _slave?.Dispose();

        // 释放 TCP 端口
        _tcpListener?.Stop();

        // 等待后台监听线程结束（最多 3 秒）
        // WaitAsync 是 .NET 6+ 的扩展方法，可以设置超时
        if (_listenTask != null)
        {
            try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { /* 超时或异常都无所谓，我们已经做了清理 */ }
        }

        AppLogger.Info("TCP 从站已停止");
    }

    // ========================================================================
    // SyncOneRegister — 实时同步单个寄存器值
    // ========================================================================

    /// <summary>
    /// 当 RegisterBank 中某个寄存器的值被修改时，实时同步到 NModbus4 的 DataStore。
    ///
    /// 【谁调用这个方法？】
    ///   RegisterBank 的 OnRegisterWritten 事件。
    ///   触发场景：用户在 UI 上修改值、定时模拟生成值、外部主站写入等。
    ///
    /// 【为什么需要同步？】
    ///   NModbus4 响应主站请求时，读的是它自己的 DataStore，不是我们的 RegisterBank。
    ///   所以只要 RegisterBank 变了，就必须立刻同步到 DataStore，
    ///   否则主站读到的还是旧值。
    ///
    /// 【线程安全】
    ///   这个方法可能在任意线程被调用（UI 线程、后台线程），
    ///   所以必须用 lock 保护 DataStore。
    /// </summary>
    private void SyncOneRegister(int address, ushort value)
    {
        lock (_dataStoreSyncRoot)
        {
            // 三重校验：
            // 1. DataStore 存在（可能还没初始化或被 Dispose 了）
            // 2. 地址在合法范围内（0~65535）
            // 3. 通过地址过滤器（如果有的话）
            if (_dataStore != null
                && (uint)address < 65536
                && (RegisterAddressFilter?.Invoke(address) ?? true))
            {
                SyncSnapshotValue(address, value);
            }
        }
    }

    // ========================================================================
    // ReplaceSnapshotValues — 批量替换快照值
    // ========================================================================

    /// <summary>
    /// 用一组新值批量替换 DataStore 中的寄存器值。
    /// 这是 IRegisterSnapshotSlaveService 接口的实现。
    ///
    /// 【什么时候用？】
    ///   1. 用户切换"监听环境"时：需要把新设备的寄存器值一次性刷到 DataStore
    ///   2. 监听启动/停止时：SlaveViewModel 推入初始值
    ///
    /// 【做了什么？】
    ///   1. 找出旧快照中有但新快照中没有的地址 → 清零（防止垃圾数据残留）
    ///   2. 把新快照中的所有地址写入 DataStore
    ///   3. 更新 _snapshotAddresses 记录（为下次替换做准备）
    ///
    /// 【线程安全】
    ///   由 SlaveViewModel 调用（UI 线程），但 DataStore 也可能被后台线程访问
    ///   （主站请求 → NModbus4 读 DataStore），所以要用 lock。
    /// </summary>
    /// <param name="values">地址→值的字典，仅包含需要更新的地址</param>
    public void ReplaceSnapshotValues(IReadOnlyDictionary<int, ushort> values)
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore == null) return;

            // 步骤 1：清除旧快照中有但新快照中没有的地址
            // 例如：上次快照覆盖了地址 100~200，这次只覆盖 100~150，
            // 那么 151~200 就要清零（否则主站会读到过期数据）
            foreach (var address in _snapshotAddresses.Except(values.Keys).ToList())
            {
                if ((uint)address < 65536)
                    SyncSnapshotValue(address, 0); // 写 0 = 清零
            }

            // 步骤 2：写入新快照值
            _snapshotAddresses.Clear();
            foreach (var (address, value) in values)
            {
                if ((uint)address < 65536)
                {
                    SyncSnapshotValue(address, value);
                    _snapshotAddresses.Add(address); // 记录这个地址被快照覆盖了
                }
            }
        }
    }

    // ========================================================================
    // ReadSnapshotValues — 读取快照值（供外部查询当前 DataStore 状态）
    // ========================================================================

    public ushort[] ReadSnapshotValues(int startAddress, int count)
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore == null || count <= 0)
                return [];

            var result = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                var address = startAddress + i;
                if ((uint)address < 65536)
                    result[i] = ReadSnapshotValue(address);
            }

            return result;
        }
    }

    // ========================================================================
    // SyncBankToDataStore — 将 RegisterBank 批量同步到 DataStore
    // ========================================================================

    /// <summary>
    /// 遍历 RegisterBank 的所有 65535 个地址，把值写入 DataStore。
    /// 只在启动时调用一次，用于初始化 DataStore。
    ///
    /// 【为什么需要这个方法？】
    ///   启动前用户可能已经在 UI 上设置了一堆寄存器值，
    ///   这些值存在 RegisterBank 里，但 DataStore 是刚创建的空白对象。
    ///   所以需要在启动时把 RegisterBank 的当前状态"刷"到 DataStore 里。
    /// </summary>
    private void SyncBankToDataStore()
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore == null) return;

            // 遍历 0~65534 共 65535 个 Holding Register 地址
            //（Modbus 协议规定 Holding Register 地址范围是 0x0000~0xFFFF）
            for (int i = 0; i < 65535; i++)
            {
                // 如果设置了地址过滤器，只同步允许的地址
                if (RegisterAddressFilter?.Invoke(i) ?? true)
                    SyncSnapshotValue(i, _bank.Read(i));
            }
        }
    }

    // ========================================================================
    // SyncSnapshotValue / ReadSnapshotValue — 读写单个 DataStore 值
    // ========================================================================

    /// <summary>
    /// 往 DataStore 里写入一个值。
    ///
    /// 【Index 转换】
    ///   Modbus 协议地址从 0 开始，但 NModbus4 的 DataStore 索引从 1 开始。
    ///   所以：dataStoreIndex = address + 1
    ///   例如：Modbus 地址 0 → DataStore 索引 1；地址 100 → 索引 101
    ///
    /// 【FunctionCode 的作用】
    ///   同一个地址 0，在不同功能码下对应不同的 DataStore 区域：
    ///   - FunctionCode=1 (Coil)        → CoilDiscretes[1]
    ///   - FunctionCode=2 (Input)       → InputDiscretes[1]
    ///   - FunctionCode=3 (Holding Reg) → HoldingRegisters[1]
    ///   - FunctionCode=4 (Input Reg)   → InputRegisters[1]
    ///   大多数 Modbus 设备只用 Holding Register（FC=3），这也是我们的默认值。
    /// </summary>
    private void SyncSnapshotValue(int address, ushort value)
    {
        // 地址 +1：NModbus4 DataStore 索引从 1 开始，而我们内部地址从 0 开始
        var dataStoreIndex = (ushort)(address + 1);

        switch (FunctionCode)
        {
            case 1:
                // Coil：布尔量，非零值 → true，零 → false
                _dataStore!.CoilDiscretes[dataStoreIndex] = value != 0;
                break;
            case 2:
                // Input Discrete：同 Coil，但只读
                _dataStore!.InputDiscretes[dataStoreIndex] = value != 0;
                break;
            case 4:
                // Input Register：只读的 16 位整数
                _dataStore!.InputRegisters[dataStoreIndex] = value;
                break;
            default:
                // Holding Register：可读写的 16 位整数（最常用）
                _dataStore!.HoldingRegisters[dataStoreIndex] = value;
                break;
        }
    }

    /// <summary>
    /// 从 DataStore 里读取一个值。
    /// 与 SyncSnapshotValue 对应，根据 FunctionCode 读取不同区域。
    /// </summary>
    private ushort ReadSnapshotValue(int address)
    {
        var dataStoreIndex = (ushort)(address + 1);

        return FunctionCode switch
        {
            1 => _dataStore!.CoilDiscretes[dataStoreIndex] ? (ushort)1 : (ushort)0,
            2 => _dataStore!.InputDiscretes[dataStoreIndex] ? (ushort)1 : (ushort)0,
            4 => _dataStore!.InputRegisters[dataStoreIndex],
            _ => _dataStore!.HoldingRegisters[dataStoreIndex]
        };
    }

    // ========================================================================
    // OnDataStoreRead — NModbus4 通知"主站来读了"
    // ========================================================================

    /// <summary>
    /// 当 TCP 客户端（主站/EMS）读取寄存器时，NModbus4 触发此事件。
    ///
    /// 【用途】
    ///   1. 记录日志：哪个主站读了什么地址
    ///   2. 触发 OnRequest 事件 → SlaveViewModel 统计请求次数
    ///
    /// 【DataStoreEventArgs】
    ///   - ModbusDataType  ：读的是哪种数据（Coil / Input / Holding Register 等）
    ///   - StartAddress    ：起始地址（NModbus4 内部索引从 1 开始，即 Modbus 地址 + 1）
    ///   - Data.A / Data.B ：读取到的数据（A = bool 数组，B = ushort 数组）
    /// </summary>
    private void OnDataStoreRead(DataStoreEventArgs e)
    {
        // 把 NModbus4 的数据类型映射成标准 Modbus 功能码
        byte functionCode = e.ModbusDataType switch
        {
            ModbusDataType.Coil          => 1,  // 读线圈
            ModbusDataType.Input         => 2,  // 读离散输入
            ModbusDataType.InputRegister => 4,  // 读输入寄存器
            _                            => 3   // 读保持寄存器（默认）
        };

        // 根据功能码选择数据数组：FC03/04 返回 ushort 数组，FC01/02 返回 bool 数组
        int count = functionCode is 3 or 4 ? e.Data.B.Count : e.Data.A.Count;

        // 通知上层（SlaveViewModel）有主站来读了
        OnRequest?.Invoke(functionCode, e.StartAddress, count, $"{ListenAddress}:{Port}");
    }

    // ========================================================================
    // OnDataStoreWritten — NModbus4 通知"主站来写了"
    // ========================================================================

    /// <summary>
    /// 当 TCP 客户端（主站/EMS）写入寄存器时，NModbus4 触发此事件。
    ///
    /// 【重要！这是数据回流的路径】
    ///   主站 → 写入 NModbus4 DataStore → 触发此事件
    ///   → 我们调用 _bank.WriteExternalRange 把值写回 RegisterBank
    ///   → RegisterBank 触发 OnExternalRegistersWritten 事件
    ///   → SlaveViewModel 收到事件 → 更新 UI 显示并高亮被修改的行
    ///
    /// 【WriteExternalRange 做了什么额外的事情？】
    ///   它不仅写值，还会：
    ///   1. 创建一个 ExternalRegisterWrite 记录（含地址、值、设备标识、来源）
    ///   2. 触发 OnExternalRegistersWritten（批量通知）
    ///   3. 触发 OnRegisterWritten（逐地址通知）
    ///   这样 UI 就能知道"有人从外部修改了这些寄存器"
    /// </summary>
    private void OnDataStoreWritten(DataStoreEventArgs e)
    {
        // 只处理 Holding Register 的写入（FC06 写单个 / FC16 写多个）
        // Coil 写入（FC05）暂不处理
        if (e.ModbusDataType == ModbusDataType.HoldingRegister)
        {
            // e.Data.B 是 ReadOnlyCollection<ushort>：NModbus4 已经帮我们解析好的值
            var regs = e.Data.B;

            // StartAddress 是 NModbus4 内部索引（从 1 开始，即 Modbus 地址 + 1）
            if ((uint)e.StartAddress < 65536)
            {
                // 计算实际要写入的寄存器数量（不能超出 65535）
                var count = Math.Min(regs.Count, 65536 - e.StartAddress);
                if (count > 0)
                {
                    // 写入 RegisterBank（会触发 OnExternalRegistersWritten 通知 UI）
                    _bank.WriteExternalRange(
                        e.StartAddress,                              // 起始地址
                        regs.Take(count).ToArray(),                  // 值数组
                        RegisterAddressFilter,                       // 地址过滤
                        BoundDeviceKey,                              // 绑定的设备标识
                        $"{ListenAddress}:{Port}");                  // 来源标识（显示在日志里）
                }
            }
        }

        // 无论是什么类型的写入，都通知上层记录日志
        // 功能码固定写 16（因为 NModbus4 不区分 FC06 和 FC16，统一按 FC16 处理）
        OnRequest?.Invoke(16, e.StartAddress, e.Data.B.Count, "TCP客户端");
    }

    // ========================================================================
    // Dispose — 资源清理
    // ========================================================================

    /// <summary>
    /// IDisposable 实现：调用 Stop 并等待最多 2 秒。
    /// 不推荐直接调用这个方法——应该用 StopAsync。
    /// </summary>
    public void Dispose() => StopAsync().Wait(2000);
}