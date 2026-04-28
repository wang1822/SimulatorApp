using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SimulatorApp.Shared.Helpers;
using SimulatorApp.Shared.Models;
using SimulatorApp.Shared.Views;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;

namespace SimulatorApp.Slave.ViewModels;

using ProtocolRow = (string ChineseName, string EnglishName, int Address, string ReadWrite, string Range, string Unit, string Note);

/// <summary>DataGrid 预览行——使用属性而非 ValueTuple 字段，WPF DataBinding 才能正常工作</summary>
public sealed class ProtocolPreviewRow
{
    public string ChineseName { get; init; } = "";
    public string EnglishName { get; init; } = "";
    public int    Address     { get; init; }
    public string ReadWrite   { get; init; } = "";
    public string Range       { get; init; } = "";
    public string Unit        { get; init; } = "";
    public string Note        { get; init; } = "";
}

/// <summary>
/// 从站「新建协议配置」对话框 ViewModel。
/// 收集设备名称、监听端口/Slave ID，以及通过粘贴/导入拿到的寄存器行。
/// </summary>
public partial class NewProtocolDialogViewModel : ObservableObject
{
    // ── 基本配置 ──────────────────────────────────────────────────────
    [ObservableProperty] private string _deviceName = "协议导入";
    [ObservableProperty] private ProtocolType _protocol = ProtocolType.Tcp;

    // TCP
    [ObservableProperty] private string _listenAddress = "0.0.0.0";
    [ObservableProperty] private int    _port = 502;

    // RTU
    [ObservableProperty] private string _comPort  = "COM3";
    [ObservableProperty] private int    _baudRate = 9600;

    // 公共
    [ObservableProperty] private byte _slaveId = 1;

    // ── 协议辅助 ──────────────────────────────────────────────────────
    public bool IsTcpMode => Protocol == ProtocolType.Tcp;
    public bool IsRtuMode => Protocol == ProtocolType.Rtu;

    public IReadOnlyList<string> ProtocolNames { get; } = ["TCP", "RTU"];
    public IReadOnlyList<int>    BaudRateOptions { get; } = [4800, 9600, 19200, 38400, 115200];

    public int ProtocolIndex
    {
        get => (int)Protocol;
        set => Protocol = (ProtocolType)value;
    }

    partial void OnProtocolChanged(ProtocolType value)
    {
        OnPropertyChanged(nameof(IsTcpMode));
        OnPropertyChanged(nameof(IsRtuMode));
        OnPropertyChanged(nameof(ProtocolIndex));
    }

    // ── 可用串口列表 ──────────────────────────────────────────────────
    public ObservableCollection<string> AvailableComPorts { get; } = new();

    [RelayCommand]
    public void RefreshComPorts()
    {
        AvailableComPorts.Clear();
        foreach (var p in SerialPort.GetPortNames()) AvailableComPorts.Add(p);
        if (AvailableComPorts.Count > 0 && !AvailableComPorts.Contains(ComPort))
            ComPort = AvailableComPorts[0];
    }

    // ── 导入的寄存器行（DataGrid 预览用） ────────────────────────────
    public ObservableCollection<ProtocolPreviewRow> Rows { get; } = new();

    [ObservableProperty] private string _rowCountText = "尚未导入数据";

    private void SetRows(IEnumerable<ProtocolRow> rows)
    {
        Rows.Clear();
        foreach (var r in rows)
            Rows.Add(new ProtocolPreviewRow
            {
                ChineseName = r.ChineseName,
                EnglishName = r.EnglishName,
                Address     = r.Address,
                ReadWrite   = r.ReadWrite,
                Range       = r.Range,
                Unit        = r.Unit,
                Note        = r.Note,
            });
        RowCountText = Rows.Count > 0 ? $"已载入 {Rows.Count} 行" : "尚未导入数据";
    }

    /// <summary>将预览行转换回 ProtocolRow 元组，供 SlaveViewModel.AddProtocolDevice 使用</summary>
    public IEnumerable<ProtocolRow> GetProtocolRows()
        => Rows.Select(r => (r.ChineseName, r.EnglishName, r.Address, r.ReadWrite, r.Range, r.Unit, r.Note));

    // ── 粘贴协议 ──────────────────────────────────────────────────────
    [RelayCommand]
    public void PasteProtocol()
    {
        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            ThemedMessageBox.Show(
                "剪贴板为空。\n请先在 Excel 协议文档中选中寄存器行（含标题行）并复制，再点此按钮。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var rows = ExcelHelper.ParseProtocolRowsFromClipboard(text);
            if (rows.Count == 0)
            {
                ThemedMessageBox.Show(
                    "未解析到有效数据行。\n请确认复制了含「Addr」标题行的协议表格（格式：Addr | 中文名 | 英文名 | R/W | 范围 | 单位 | 备注）。",
                    "解析失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SetRows(rows);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"粘贴协议解析失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── 导入 Excel ────────────────────────────────────────────────────
    [RelayCommand]
    public void ImportExcel()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel 文件|*.xlsx;*.xls",
            Title  = "选择协议文档 Excel（如 MPPT_Modbus_V1.0.xlsx）"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (name, rows) = ExcelHelper.ParseProtocolRowsFromFile(dlg.FileName);
            if (rows.Count == 0)
            {
                ThemedMessageBox.Show(
                    "文件中未找到有效协议数据行。\n请确认文件格式：Addr | 中文名 | 英文名 | R/W | 范围 | 单位 | 备注。",
                    "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(DeviceName) || DeviceName == "协议导入")
                DeviceName = name;
            SetRows(rows);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"导入 Excel 失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── 对话框结果 ────────────────────────────────────────────────────
    [ObservableProperty] private bool? _dialogResult;

    [RelayCommand]
    public void Confirm()
    {
        if (Rows.Count == 0)
        {
            ThemedMessageBox.Show(
                "请先粘贴或导入协议数据，再点「确定」。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    [RelayCommand]
    public void Cancel() => DialogResult = false;
}
