using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly HashSet<InspectorRow> _valueTrackedInspectorRows = new();
    private int _editingImportedDeviceId;

    private void InlineProtocolDraftGrid_Loaded(object sender, RoutedEventArgs e)
    {
        BindProtocolDraftRowsToInspector();
    }

    private void BindProtocolDraftRowsToInspector()
    {
        if (DataContext is not RegisterInspectorViewModel inspectorVm)
        {
            UnbindProtocolDraftRowsFromInspector();
            ProtocolDraftRows.Clear();
            return;
        }

        if (!ReferenceEquals(_boundInspectorVm, inspectorVm))
        {
            if (_boundInspectorVm is not null)
            {
                _boundInspectorVm.Rows.CollectionChanged -= InspectorRows_CollectionChanged;
            }

            UnsubscribeInspectorRowValueChanges();
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
                existing.Value = inspectorRow.Value;
                rebuilt.Add(existing);
                continue;
            }

            rebuilt.Add(new InlineProtocolDraftRow
            {
                Address = inspectorRow.Address,
                Value = inspectorRow.Value,
                Note = inspectorRow.Note ?? string.Empty
            });
        }

        SyncInspectorRowValueSubscriptions();
        ProtocolDraftRows.ReplaceWith(rebuilt);
    }

    private void InspectorRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectorRow.Value) || sender is not InspectorRow inspectorRow)
        {
            return;
        }

        var draftRow = ProtocolDraftRows.FirstOrDefault(x => x.Address == inspectorRow.Address);
        if (draftRow is not null)
        {
            draftRow.Value = inspectorRow.Value;
        }

        SyncVisibleCurrentValueText(inspectorRow.Address, inspectorRow.Value);
    }

    public string? GetDraftChineseNameForAddress(int address)
        => ProtocolDraftRows.FirstOrDefault(x => x.Address == address)?.ChineseName;

    public void MarkExternalWrite(int address, ushort value)
    {
        var draftRow = ProtocolDraftRows.FirstOrDefault(x => x.Address == address);
        if (draftRow is not null)
        {
            draftRow.Value = value;
            draftRow.IsExternallyWritten = true;
        }
    }

    public void ClearExternalWriteHighlights()
    {
        foreach (var row in ProtocolDraftRows)
            row.IsExternallyWritten = false;
    }

    private void SyncInspectorRowValueSubscriptions()
    {
        UnsubscribeInspectorRowValueChanges();
        if (_boundInspectorVm is null)
        {
            return;
        }

        foreach (var row in _boundInspectorVm.Rows)
        {
            row.PropertyChanged += InspectorRow_PropertyChanged;
            _valueTrackedInspectorRows.Add(row);
        }
    }

    private void UnsubscribeInspectorRowValueChanges()
    {
        foreach (var row in _valueTrackedInspectorRows)
        {
            row.PropertyChanged -= InspectorRow_PropertyChanged;
        }

        _valueTrackedInspectorRows.Clear();
    }

    private void UnbindProtocolDraftRowsFromInspector()
    {
        if (_boundInspectorVm is not null)
        {
            _boundInspectorVm.Rows.CollectionChanged -= InspectorRows_CollectionChanged;
        }

        UnsubscribeInspectorRowValueChanges();
        _boundInspectorVm = null;
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
                Value = r.CurrentValueRaw,
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

        if (await slaveVm.DeviceNameExistsAsync(deviceName, _editingImportedDeviceId))
        {
            InlineSaveErrorTextBlock.Text = "已有该设备名称。";
            return;
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
            DeviceName = deviceName
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

public sealed class InlineProtocolDraftRow : INotifyPropertyChanged
{
    private int _address;
    private ushort _value;
    private string _chineseName = string.Empty;
    private string _englishName = string.Empty;
    private string _unit = string.Empty;
    private string _range = string.Empty;
    private string _note = string.Empty;
    private bool _isExternallyWritten;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Address
    {
        get => _address;
        set => SetProperty(ref _address, value, nameof(Address));
    }

    public ushort Value
    {
        get => _value;
        set => SetProperty(ref _value, value, nameof(Value));
    }

    public string ChineseName
    {
        get => _chineseName;
        set => SetProperty(ref _chineseName, value ?? string.Empty, nameof(ChineseName));
    }

    public string EnglishName
    {
        get => _englishName;
        set => SetProperty(ref _englishName, value ?? string.Empty, nameof(EnglishName));
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value ?? string.Empty, nameof(Unit));
    }

    public string Range
    {
        get => _range;
        set => SetProperty(ref _range, value ?? string.Empty, nameof(Range));
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value ?? string.Empty, nameof(Note));
    }

    public bool IsExternallyWritten
    {
        get => _isExternallyWritten;
        set => SetProperty(ref _isExternallyWritten, value, nameof(IsExternallyWritten));
    }

    private void SetProperty<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
