using SimulatorApp.Shared.Logging;
using SimulatorApp.Slave.ViewModels;
using SimulatorApp.Slave.Views;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SimulatorApp.Slave.Views.Panels;

public partial class SlavePanel : UserControl
{
    private static bool _dbAutoRefreshedOnce;
    private bool _isSyncingDeviceSelections;
    private SlaveViewModel? _diagnosticVm;
    private bool _listenerDiagnosticsAttached;
    private SlaveViewModel? _logVm;
    private bool _logEntriesAttached;

    public SlavePanel()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded_WithAutoDbRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SlaveViewModel vm)
            return;

        EnsureListenerDiagnosticsAttached(vm);
        EnsureLogAutoScrollAttached(vm);

        if (_dbAutoRefreshedOnce)
            return;

        _dbAutoRefreshedOnce = true;

        try
        {
            await vm.RefreshDbAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[SYS] auto-refresh-db failed: {ex.Message}");
        }
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachListenerDiagnostics();
        DetachLogAutoScroll();
        // Keep listeners managed by ViewModel lifecycle.
    }

    private async void NewProtocol_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SlaveViewModel vm)
            return;

        var dlgVm = new NewProtocolDialogViewModel();
        dlgVm.RefreshComPorts();

        var dlg = new NewProtocolDialog(dlgVm)
        {
            Owner = Window.GetWindow(this)
        };

        dlg.ShowDialog();
        if (dlgVm.DialogResult != true || dlgVm.Rows.Count == 0)
            return;

        await vm.AddDeviceFromDialogAsync(dlgVm);
    }

    private void DeleteImportedProtocol_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SlaveViewModel vm)
            return;

        if (ImportedDevicesList.SelectedItem is not ImportedDeviceViewModel selected)
            return;

        if (vm.RemoveImportedDeviceCommand.CanExecute(selected))
            vm.RemoveImportedDeviceCommand.Execute(selected);
    }

    private void ImportedDeviceNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
            return;

        if (sender is not FrameworkElement { DataContext: ImportedDeviceViewModel vm })
            return;

        vm.BeginRename();
        e.Handled = true;
    }

    private void ImportedDeviceNameEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox editor)
            FocusImportedDeviceNameEditor(editor);
    }

    private async void ImportedDeviceNameEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ImportedDeviceViewModel vm })
            await vm.CommitRenameAsync();
    }

    private async void ImportedDeviceNameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ImportedDeviceViewModel vm })
            return;

        if (e.Key == Key.Enter)
        {
            await vm.CommitRenameAsync();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRename();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void FocusImportedDeviceNameEditor(TextBox editor)
    {
        editor.Dispatcher.BeginInvoke(() =>
        {
            if (!editor.IsVisible)
                return;

            editor.Focus();
            editor.SelectAll();
            Keyboard.Focus(editor);
        }, DispatcherPriority.Input);
    }

    private async void StartAllListeners_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SlaveViewModel vm)
            return;

        await vm.StartAllListenersAsync();
        LogListenerSnapshot(vm, "start-all");
    }

    private async void StopAllListeners_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SlaveViewModel vm)
            return;

        await vm.StopAllListenersAsync();
        LogListenerSnapshot(vm, "stop-all");
    }

    private async void ToggleListener_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SlaveViewModel vm)
            return;
        if (sender is not Button { DataContext: SlaveListenerConfig listener })
            return;

        await vm.ToggleListenerAsync(listener);
        LogListenerSnapshot(vm, "toggle-single");
    }

    private void RtuComPortCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is SlaveViewModel vm)
            vm.RefreshComPorts();
    }

    private void DeviceListChild_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DeviceListScroller == null)
            return;

        // 将内层列表滚轮统一转发给外层滚动容器，避免“必须拖动滚动条”。
        double targetOffset = DeviceListScroller.VerticalOffset - (e.Delta / 3.0);
        targetOffset = Math.Max(0, Math.Min(targetOffset, DeviceListScroller.ScrollableHeight));
        DeviceListScroller.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private void BuiltinDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => HandleDeviceSelectionChanged(sender as ListBox);

    private void ImportedDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => HandleDeviceSelectionChanged(sender as ListBox);

    private void InspectorDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => HandleDeviceSelectionChanged(sender as ListBox);

    private void HandleDeviceSelectionChanged(ListBox? sourceList)
    {
        if (sourceList == null)
            return;

        if (_isSyncingDeviceSelections)
            return;

        if (DataContext is not SlaveViewModel vm)
            return;

        if (sourceList.SelectedItem is not DeviceViewModelBase selectedVm)
            return;

        SyncDeviceSelections(sourceList);

        if (selectedVm is RegisterInspectorViewModel)
            ClearAllSimulatingChecks(vm);

        if (vm.Listeners.Any(l => l.IsRunning))
        {
            // 监听运行中允许切换右侧设备展示，但不触发监听配置切换。
            SetSelectedDeviceWithoutProfileSwitch(vm, selectedVm);
            AppLogger.Info("[SYS] listener-profile-switch blocked: running listener exists");
            return;
        }

        vm.SelectedDevice = selectedVm;
    }

    private void SyncDeviceSelections(ListBox sourceList)
    {
        try
        {
            _isSyncingDeviceSelections = true;

            if (!ReferenceEquals(sourceList, BuiltinDevicesList))
                BuiltinDevicesList.SelectedItem = null;

            if (!ReferenceEquals(sourceList, ImportedDevicesList))
                ImportedDevicesList.SelectedItem = null;

            if (!ReferenceEquals(sourceList, InspectorDevicesList))
                InspectorDevicesList.SelectedItem = null;
        }
        finally
        {
            _isSyncingDeviceSelections = false;
        }
    }

    private static void ClearAllSimulatingChecks(SlaveViewModel vm)
    {
        foreach (var device in vm.BuiltinDevices.Where(d => d.IsSimulating))
            device.IsSimulating = false;

        foreach (var device in vm.ImportedDevices.Where(d => d.IsSimulating))
            device.IsSimulating = false;

        AppLogger.Info("[SYS] inspector-selected: cleared all simulating checks");
    }

    private static void LogListenerSnapshot(SlaveViewModel vm, string scene)
    {
        AppLogger.Info($"[LST] snapshot scene={scene} total={vm.Listeners.Count}");

        foreach (var listener in vm.Listeners)
        {
            bool shouldRun = TryInvokeShouldListenerBeActive(vm, listener);
            int activeDevices = TryGetActiveDeviceCount(vm, listener);
            string endpoint = listener.IsTcpMode
                ? $"{listener.ListenAddress}:{listener.Port}"
                : $"{listener.ComPort}@{listener.BaudRate}";

            AppLogger.Info(
                $"[LST] scene={scene} dbId={listener.DbId} protocol={listener.Protocol} endpoint={endpoint} " +
                $"enabled={listener.IsEnabled} running={listener.IsRunning} shouldRun={shouldRun} activeDevices={activeDevices}");
        }
    }

    private static bool TryInvokeShouldListenerBeActive(SlaveViewModel vm, SlaveListenerConfig listener)
    {
        try
        {
            var method = typeof(SlaveViewModel).GetMethod(
                "ShouldListenerBeActive",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(SlaveListenerConfig)],
                modifiers: null);

            if (method?.Invoke(vm, [listener]) is bool result)
                return result;
        }
        catch
        {
            // Keep fallback behavior.
        }

        return listener.IsEnabled;
    }

    private static int TryGetActiveDeviceCount(SlaveViewModel vm, SlaveListenerConfig listener)
    {
        try
        {
            var method = typeof(SlaveViewModel).GetMethod(
                "GetActiveDevicesForListener",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(SlaveListenerConfig)],
                modifiers: null);

            var value = method?.Invoke(vm, [listener]);
            if (value is ICollection collection)
                return collection.Count;
        }
        catch
        {
            // Keep fallback behavior.
        }

        return vm.BuiltinDevices.Count(d => d.IsSimulating) + vm.ImportedDevices.Count(d => d.IsSimulating);
    }

    private void EnsureListenerDiagnosticsAttached(SlaveViewModel vm)
    {
        if (_listenerDiagnosticsAttached && ReferenceEquals(_diagnosticVm, vm))
            return;

        DetachListenerDiagnostics();

        _diagnosticVm = vm;
        _diagnosticVm.Listeners.CollectionChanged += Listeners_CollectionChanged;

        foreach (var listener in _diagnosticVm.Listeners)
            listener.PropertyChanged += Listener_PropertyChanged;

        _listenerDiagnosticsAttached = true;
        LogListenerSnapshot(_diagnosticVm, "diag-attach");
    }

    private void DetachListenerDiagnostics()
    {
        if (!_listenerDiagnosticsAttached || _diagnosticVm == null)
            return;

        _diagnosticVm.Listeners.CollectionChanged -= Listeners_CollectionChanged;
        foreach (var listener in _diagnosticVm.Listeners)
            listener.PropertyChanged -= Listener_PropertyChanged;

        _diagnosticVm = null;
        _listenerDiagnosticsAttached = false;
    }

    private void Listeners_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_diagnosticVm == null)
            return;

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<SlaveListenerConfig>())
                item.PropertyChanged -= Listener_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<SlaveListenerConfig>())
                item.PropertyChanged += Listener_PropertyChanged;
        }

        LogListenerSnapshot(_diagnosticVm, "listeners-changed");
    }

    private void Listener_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_diagnosticVm == null || sender is not SlaveListenerConfig listener)
            return;

        if (e.PropertyName is nameof(SlaveListenerConfig.IsRunning)
            or nameof(SlaveListenerConfig.IsEnabled)
            or nameof(SlaveListenerConfig.ListenAddress)
            or nameof(SlaveListenerConfig.Port)
            or nameof(SlaveListenerConfig.ComPort)
            or nameof(SlaveListenerConfig.BaudRate)
            or nameof(SlaveListenerConfig.Protocol))
        {
            string endpoint = listener.IsTcpMode
                ? $"{listener.ListenAddress}:{listener.Port}"
                : $"{listener.ComPort}@{listener.BaudRate}";

            AppLogger.Info(
                $"[LST] changed prop={e.PropertyName} dbId={listener.DbId} protocol={listener.Protocol} " +
                $"endpoint={endpoint} enabled={listener.IsEnabled} running={listener.IsRunning}");

            LogListenerSnapshot(_diagnosticVm, $"listener-{e.PropertyName}");
        }
    }

    private void EnsureLogAutoScrollAttached(SlaveViewModel vm)
    {
        if (_logEntriesAttached && ReferenceEquals(_logVm, vm))
            return;

        DetachLogAutoScroll();

        _logVm = vm;
        _logVm.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
        _logEntriesAttached = true;
        ScrollLogToEndAsync();
    }

    private void DetachLogAutoScroll()
    {
        if (!_logEntriesAttached || _logVm == null)
            return;

        _logVm.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
        _logVm = null;
        _logEntriesAttached = false;
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Reset
            or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Move)
        {
            ScrollLogToEndAsync();
        }
    }

    private void ScrollLogToEndAsync()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke((Action)ScrollLogToEndAsync, DispatcherPriority.Background);
            return;
        }

        Dispatcher.BeginInvoke(
            (Action)(() => LogScrollViewer?.ScrollToEnd()),
            DispatcherPriority.Background);
    }

    private static void SetSelectedDeviceWithoutProfileSwitch(SlaveViewModel vm, DeviceViewModelBase selectedVm)
    {
        try
        {
            // ObservableProperty 生成字段：_selectedDevice
            var field = typeof(SlaveViewModel).GetField("_selectedDevice", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(vm, selectedVm);

            // 手动刷新界面依赖属性，不触发 OnSelectedDeviceChanged 监听配置逻辑。
            var notify = typeof(SlaveViewModel).GetMethod(
                "OnPropertyChanged",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string)],
                modifiers: null);

            notify?.Invoke(vm, ["SelectedDevice"]);
            notify?.Invoke(vm, ["SelectedDevicePanel"]);
            notify?.Invoke(vm, ["HasSelectedDevice"]);
            notify?.Invoke(vm, ["CanToggleDeviceSimulation"]);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[SYS] set-selected-device-without-profile-switch failed: {ex.Message}");
            vm.SelectedDevice = selectedVm;
        }
    }
}
