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
    public ObservableCollection<InlineProtocolDraftRow> ProtocolDraftRows { get; } = new();
    private RegisterInspectorViewModel? _boundInspectorVm;

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

        ProtocolDraftRows.Clear();
        foreach (var row in rebuilt)
        {
            ProtocolDraftRows.Add(row);
        }
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
            InlineSaveErrorTextBlock.Text = "至少需要一条有效寄存器定义（地址必填）。";
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
            await slaveVm.AddDeviceFromDialogAsync(dialogVm);
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
