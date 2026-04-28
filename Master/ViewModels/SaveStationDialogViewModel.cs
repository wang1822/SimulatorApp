using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SimulatorApp.Master.Models;
using SimulatorApp.Master.Services;
using SimulatorApp.Shared.Models;
using SimulatorApp.Shared.Views;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;

namespace SimulatorApp.Master.ViewModels;

/// <summary>
/// 保存配置对话框 ViewModel。
/// 收集站点信息 + 遥测/遥控寄存器配置，由 MasterViewModel 负责写入 DB。
/// </summary>
public partial class SaveStationDialogViewModel : ObservableObject
{
    // ── 站点基本信息 ──────────────────────────────────────────────────────
    [ObservableProperty] private string _stationName   = string.Empty;
    [ObservableProperty] private int    _protocol      = 0; // 0=TCP 1=RTU
    [ObservableProperty] private string _host          = "172.168.3.100";
    [ObservableProperty] private int    _port          = 502;
    [ObservableProperty] private string _portName      = "COM3";
    [ObservableProperty] private int    _baudRate      = 9600;
    [ObservableProperty] private byte   _slaveId       = 1;
    [ObservableProperty] private int    _pollIntervalMs = 1000;

    public bool IsTcpMode => Protocol == 0;
    public bool IsRtuMode => Protocol == 1;
    partial void OnProtocolChanged(int value)
    {
        OnPropertyChanged(nameof(IsTcpMode));
        OnPropertyChanged(nameof(IsRtuMode));
    }

    // ── 遥测 / 遥控行 ─────────────────────────────────────────────────────
    public ObservableCollection<RegisterConfigEditRow> TelemeterRows { get; } = new();
    public ObservableCollection<RegisterConfigEditRow> ControlRows   { get; } = new();

    // ── 辅助 ─────────────────────────────────────────────────────────────
    public ObservableCollection<string> AvailableComPorts { get; } = new();
    public IReadOnlyList<int> BaudRateOptions { get; } = new[] { 4800, 9600, 19200, 38400, 115200 };

    /// <summary>对话框是否已成功保存（关闭后供调用方判断）</summary>
    [ObservableProperty] private bool _dialogResult = false;

    // ── 构造 ─────────────────────────────────────────────────────────────

    public SaveStationDialogViewModel() => RefreshPorts();

    /// <summary>从已有站点填充表单（编辑模式）</summary>
    public void LoadFromStation(MasterStation station, List<MasterRegisterConfig> configs)
    {
        StationName    = station.Name;
        Protocol       = station.Protocol;
        Host           = station.Host;
        Port           = station.Port;
        PortName       = station.PortName;
        BaudRate       = station.BaudRate;
        SlaveId        = station.SlaveId;
        PollIntervalMs = station.PollIntervalMs;

        TelemeterRows.Clear();
        ControlRows.Clear();
        foreach (var cfg in configs)
        {
            var row = RegisterConfigEditRow.FromModel(cfg);
            if (cfg.Category == 0) TelemeterRows.Add(row);
            else                   ControlRows.Add(row);
        }
    }

    // ── 命令：遥测 Tab ────────────────────────────────────────────────────

    [RelayCommand]
    public void AddTelemeterRow()
        => TelemeterRows.Add(new RegisterConfigEditRow { Category = 0 });

    [RelayCommand]
    public void RemoveTelemeterRow(RegisterConfigEditRow? row)
    {
        if (row != null) TelemeterRows.Remove(row);
    }

    [RelayCommand]
    public void ImportTelemeterExcel()
        => ImportExcel(TelemeterRows, 0);

    [RelayCommand]
    public void PasteTelemeter()
        => PasteRows(TelemeterRows, 0);

    // ── 命令：遥控 Tab ────────────────────────────────────────────────────

    [RelayCommand]
    public void AddControlRow()
        => ControlRows.Add(new RegisterConfigEditRow { Category = 1, ReadWrite = "R/W" });

    [RelayCommand]
    public void RemoveControlRow(RegisterConfigEditRow? row)
    {
        if (row != null) ControlRows.Remove(row);
    }

    [RelayCommand]
    public void ImportControlExcel()
        => ImportExcel(ControlRows, 1);

    [RelayCommand]
    public void PasteControl()
        => PasteRows(ControlRows, 1);

    // ── 命令：保存 / 取消 ────────────────────────────────────────────────

    [RelayCommand]
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(StationName))
        {
            ThemedMessageBox.Show("站点名称不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    [RelayCommand]
    public void Cancel() => DialogResult = false;

    // ── 辅助 ─────────────────────────────────────────────────────────────

    /// <summary>将表单数据转换为站点对象（供 MasterViewModel 保存到 DB）</summary>
    public (MasterStation Station, List<MasterRegisterConfig> Configs) BuildResult()
    {
        var station = new MasterStation
        {
            Name           = StationName.Trim(),
            Protocol       = Protocol,
            Host           = Host,
            Port           = Port,
            PortName       = PortName,
            BaudRate       = BaudRate,
            SlaveId        = SlaveId,
            PollIntervalMs = PollIntervalMs
        };
        var configs = new List<MasterRegisterConfig>();
        foreach (var r in TelemeterRows) { r.Category = 0; configs.Add(r.ToModel(0)); }
        foreach (var r in ControlRows)   { r.Category = 1; configs.Add(r.ToModel(0)); }
        return (station, configs);
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        AvailableComPorts.Clear();
        foreach (var p in SerialPort.GetPortNames())
            AvailableComPorts.Add(p);
    }

    private void ImportExcel(ObservableCollection<RegisterConfigEditRow> target, int category)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel 文件|*.xlsx;*.xls",
            Title  = "选择通讯协议 Excel（列：起始地址|数量|变量名|中文名|读写|单位|数据类型|寄存器类型|比例系数|偏移量|取值范围|说明）"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var configs = MasterExcelHelper.ImportRegisterConfigs(dlg.FileName, category);
            foreach (var cfg in configs)
                target.Add(RegisterConfigEditRow.FromModel(cfg));
            ThemedMessageBox.Show($"导入成功，共 {configs.Count} 行。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PasteRows(ObservableCollection<RegisterConfigEditRow> target, int category)
    {
        var tsv = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(tsv)) return;
        // 同时读取 HTML 格式，用于识别删除线行
        var html = Clipboard.GetData(DataFormats.Html) as string;
        try
        {
            var configs = MasterExcelHelper.ImportFromClipboard(tsv, html, category);
            foreach (var cfg in configs)
                target.Add(RegisterConfigEditRow.FromModel(cfg));
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"粘贴解析失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
