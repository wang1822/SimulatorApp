using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SimulatorApp.Slave.ViewModels;

namespace SimulatorApp.Slave.Views.Panels;

public partial class RegisterInspectorPanel
{
    public BulkObservableCollection<InlineProtocolDraftRow> ProtocolDraftRows { get; } = new();
    private RegisterInspectorViewModel? _boundInspectorVm;
    private int _editingImportedDeviceId;

    private void InlineProtocolDraftGrid_Loaded(object sender, RoutedEventArgs e)
    {
        BindProtocolDraftRowsToInspector();
    }

    private void BindProtocolDraftRowsToInspector()
    {
        if (DataContext is not RegisterInspectorViewModel inspectorVm)
        {
            ProtocolDraftRows.Clear();
            return;
        }

        if (!ReferenceEquals(_boundInspectorVm, inspectorVm))
        {
            if (_boundInspectorVm is not null)
            {
                _boundInspectorVm.Rows.CollectionChanged -= InspectorRows_CollectionChanged;
            }

            _boundInspectorVm = inspectorVm;
            _boundInspectorVm.Rows.CollectionChanged += InspectorRows_CollectionChanged;
        }

        RebuildProtocolDraftRowsFromInspector();
    }

    private void InspectorRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildProtocolDraftRowsFromInspector();
    }

    private void RebuildProtocolDraftRowsFromInspector()
    {
        if (_boundInspectorVm is null)
        {
            ProtocolDraftRows.Clear();
            return;
        }

        var oldRows = ProtocolDraftRows.ToDictionary(x => x.Address, x => x);
        var rebuilt = new List<InlineProtocolDraftRow>();

        foreach (var inspectorRow in _boundInspectorVm.Rows.OrderBy(x => x.Address))
        {
            if (oldRows.TryGetValue(inspectorRow.Address, out var existing))
            {
                existing.Address = inspectorRow.Address;
                rebuilt.Add(existing);
                continue;
            }

            rebuilt.Add(new InlineProtocolDraftRow
            {
                Address = inspectorRow.Address,
                Note = inspectorRow.Note ?? string.Empty
            });
        }

        ProtocolDraftRows.ReplaceWith(rebuilt);
    }

    public void LoadImportedDeviceForEdit(ImportedDeviceViewModel imported)
    {
        if (DataContext is not RegisterInspectorViewModel inspectorVm)
            return;

        _editingImportedDeviceId = imported.DbId;
        InlineDeviceNameBox.Text = imported.DeviceName;

        inspectorVm.LoadRowsForProtocolEdit(imported.Rows
            .Where(r => !r.IsPending)
            .Select(r => (r.Address, r.CurrentValueRaw, r.Note ?? string.Empty)));

        BindProtocolDraftRowsToInspector();
        var draftRows = imported.Rows
            .Where(r => !r.IsPending)
            .OrderBy(r => r.Address)
            .Select(r => new InlineProtocolDraftRow
            {
                Address = r.Address,
                ChineseName = r.ChineseName ?? string.Empty,
                EnglishName = r.EnglishName ?? string.Empty,
                Unit = r.Unit ?? string.Empty,
                Range = r.Range ?? string.Empty,
                Note = r.Note ?? string.Empty
            })
            .ToList();

        ProtocolDraftRows.ReplaceWith(draftRows);
        foreach (var row in draftRows)
        {
            // 编辑导入协议时当前值来自原设备，不能再走“新增地址默认 0”的初始化。
            if (!_defaultedCurrentValueDraftRows.TryGetValue(row, out _))
                _defaultedCurrentValueDraftRows.Add(row, CurrentValueDefaultedMarker);
        }

        InlineSaveErrorTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
        InlineSaveErrorTextBlock.Text = $"正在编辑“{imported.DeviceName}”，保存后会替换原协议。";
    }

    private async void SaveInspectorAsProtocol_Click(object sender, RoutedEventArgs e)
    {
        BindProtocolDraftRowsToInspector();
        InlineSaveErrorTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
        InlineSaveErrorTextBlock.Text = string.Empty;

        var deviceName = InlineDeviceNameBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            InlineSaveErrorTextBlock.Text = "设备名称为必填。";
            return;
        }

        var slaveVm = FindAncestorDataContext<SlaveViewModel>(this);
        if (slaveVm is null)
        {
            InlineSaveErrorTextBlock.Text = "未找到从站主上下文，无法保存。";
            return;
        }

        var defaultListener = slaveVm.Listeners.FirstOrDefault(x => x.IsEnabled)
                              ?? slaveVm.Listeners.FirstOrDefault();
        if (defaultListener is null)
        {
            InlineSaveErrorTextBlock.Text = "请先在上方创建默认监听配置。";
            return;
        }

        var protocolIndex = Math.Max(0, defaultListener.ProtocolIndex);
        var slaveId = defaultListener.SlaveId;
        var listenAddress = defaultListener.ListenAddress?.Trim() ?? string.Empty;
        var port = defaultListener.Port;

        if (protocolIndex == 0)
        {
            if (string.IsNullOrWhiteSpace(listenAddress))
            {
                InlineSaveErrorTextBlock.Text = "默认监听配置的监听地址为空，请先在上方监听配置中填写。";
                return;
            }

            if (port < 1 || port > 65535)
            {
                InlineSaveErrorTextBlock.Text = "默认监听配置的端口无效，请先在上方监听配置中填写。";
                return;
            }
        }

        if (ProtocolDraftRows.Count == 0)
        {
            InlineSaveErrorTextBlock.Text = "请先使用“批量加载”或“添加单地址”生成寄存器地址。";
            return;
        }

        var previewRows = new List<ProtocolPreviewRow>();
        var rowIndex = 0;

        foreach (var row in ProtocolDraftRows)
        {
            rowIndex++;
            var chineseName = row.ChineseName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(chineseName))
            {
                InlineSaveErrorTextBlock.Text = $"第 {rowIndex} 行中文名为必填。";
                return;
            }

            previewRows.Add(new ProtocolPreviewRow
            {
                Address = row.Address,
                ChineseName = chineseName,
                EnglishName = row.EnglishName?.Trim() ?? string.Empty,
                Unit = row.Unit?.Trim() ?? string.Empty,
                Range = row.Range?.Trim() ?? string.Empty,
                Note = row.Note?.Trim() ?? string.Empty,
                ReadWrite = "RW"
            });
        }

        if (previewRows.Count == 0)
        {
            InlineSaveErrorTextBlock.Text = "至少需要一条有效寄存器定义（地址、中文名必填）。";
            return;
        }

        var dialogVm = new NewProtocolDialogViewModel
        {
            DeviceName = deviceName,
            ProtocolIndex = protocolIndex,
            SlaveId = slaveId,
            ListenAddress = listenAddress,
            Port = port,
            ComPort = defaultListener.ComPort,
            BaudRate = defaultListener.BaudRate
        };

        dialogVm.Rows.Clear();
        foreach (var row in previewRows)
        {
            dialogVm.Rows.Add(row);
        }

        try
        {
            if (_editingImportedDeviceId > 0)
            {
                var currentValues = _boundInspectorVm is null
                    ? new Dictionary<int, ushort>()
                    : _boundInspectorVm.Rows
                        .GroupBy(r => r.Address)
                        .ToDictionary(g => g.Key, g => g.First().Value);

                await slaveVm.ReplaceImportedDeviceFromInspectorAsync(_editingImportedDeviceId, dialogVm, currentValues);
                _editingImportedDeviceId = 0;
                ClearInspectorDraftAfterSave();
                InlineSaveErrorTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
                InlineSaveErrorTextBlock.Text = "保存成功，已替换原协议设备。";
                MessageBox.Show("保存成功，已替换原协议设备。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await slaveVm.AddDeviceFromDialogAsync(dialogVm);
            ClearInspectorDraftAfterSave();
            InlineSaveErrorTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
            InlineSaveErrorTextBlock.Text = "保存成功，已加入“协议导入设备”区域。";
            MessageBox.Show("保存成功，已加入“协议导入设备”区域。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            InlineSaveErrorTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
            InlineSaveErrorTextBlock.Text = $"保存失败：{ex.Message}";
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearInspectorDraftAfterSave()
    {
        _boundInspectorVm?.Rows.Clear();
        ProtocolDraftRows.Clear();
        InlineDeviceNameBox.Text = string.Empty;
        _defaultedCurrentValueDraftRows.Clear();
    }

    private static TContext? FindAncestorDataContext<TContext>(DependencyObject? start)
        where TContext : class
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is TContext context)
            {
                return context;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        if (Application.Current is not null)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext is TContext directContext)
                {
                    return directContext;
                }

                var dataContext = window.DataContext;
                if (dataContext is null)
                {
                    continue;
                }

                var slaveVmProperty = dataContext.GetType().GetProperty("SlaveVm", BindingFlags.Public | BindingFlags.Instance);
                if (slaveVmProperty?.GetValue(dataContext) is TContext nestedContext)
                {
                    return nestedContext;
                }
            }
        }

        return null;
    }
}

public sealed class InlineProtocolDraftRow
{
    public int Address { get; set; }

    public string ChineseName { get; set; } = string.Empty;

    public string EnglishName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Range { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}
