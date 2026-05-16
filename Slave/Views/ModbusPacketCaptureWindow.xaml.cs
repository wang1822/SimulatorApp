using Microsoft.Win32;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.ViewModels;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SimulatorApp.Slave.Views;

public partial class ModbusPacketCaptureWindow : Window
{
    private const int ClipboardRetryCount = 30;
    private const int ClipboardRetryDelayMs = 100;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    private ModbusPacketCaptureViewModel? _viewModel;

    public ModbusPacketCaptureWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();

        _viewModel = e.NewValue as ModbusPacketCaptureViewModel;
        if (_viewModel is not null)
            _viewModel.Entries.CollectionChanged += Entries_CollectionChanged;
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
            _viewModel.Entries.CollectionChanged -= Entries_CollectionChanged;

        _viewModel = null;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsCapturing || _viewModel.IsPaused)
            return;

        Dispatcher.BeginInvoke((Action)ScrollVisiblePacketsToEnd, DispatcherPriority.Background);
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Dispatcher.BeginInvoke((Action)(async () => await CopySelectedToClipboardAsync()), DispatcherPriority.ContextIdle);
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
        => ExportSelectedToTextFile();

    private void SourceFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => Dispatcher.BeginInvoke((Action)ScrollVisiblePacketsToEnd, DispatcherPriority.Background);

    private void ScrollVisiblePacketsToEnd()
    {
        if (PacketList.Items.Count > 0)
            PacketList.ScrollIntoView(PacketList.Items[PacketList.Items.Count - 1]);
    }

    private async Task CopySelectedToClipboardAsync()
    {
        var entries = GetEntriesForOutput();
        if (entries.Count == 0)
            return;

        var text = BuildExportText(entries);
        var ownerHandle = new WindowInteropHelper(this).Handle;
        var result = await TrySetClipboardTextAsync(ownerHandle, text);
        if (result.Success)
            return;

        MessageBox.Show(this,
            $"复制失败：系统剪贴板长时间被其他程序占用。\n请稍后再试，或关闭剪贴板增强/远程同步工具。\n\n{result.Error}",
            "报文抓取",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ExportSelectedToTextFile()
    {
        var entries = GetEntriesForOutput();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的报文。", "报文抓取", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出报文抓取记录",
            FileName = $"报文抓取_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, BuildExportText(entries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：{ex.Message}", "报文抓取", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<ModbusPacketCaptureEntry> GetEntriesForOutput()
    {
        var selectedEntries = PacketList.SelectedItems
            .OfType<ModbusPacketCaptureEntry>()
            .ToList();

        return selectedEntries.Count > 0
            ? selectedEntries
            : PacketList.Items.OfType<ModbusPacketCaptureEntry>().ToList();
    }

    private static string BuildExportText(IEnumerable<ModbusPacketCaptureEntry> entries)
        => string.Join(Environment.NewLine, entries.Select(entry => entry.Text));

    private static async Task<(bool Success, string Error)> TrySetClipboardTextAsync(IntPtr ownerHandle, string text)
    {
        var lastError = string.Empty;

        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            if (TrySetClipboardTextOnce(ownerHandle, text, out lastError))
                return (true, string.Empty);

            await Task.Delay(ClipboardRetryDelayMs);
        }

        return (false, string.IsNullOrWhiteSpace(lastError) ? "未知剪贴板错误" : lastError);
    }

    private static bool TrySetClipboardTextOnce(IntPtr ownerHandle, string text, out string error)
    {
        var clipboardOpened = false;
        var globalHandle = IntPtr.Zero;

        try
        {
            var bytes = Encoding.Unicode.GetBytes(text);
            globalHandle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)(bytes.Length + 2));
            if (globalHandle == IntPtr.Zero)
            {
                error = GetLastWin32ErrorMessage("GlobalAlloc");
                return false;
            }

            var dataPointer = GlobalLock(globalHandle);
            if (dataPointer == IntPtr.Zero)
            {
                error = GetLastWin32ErrorMessage("GlobalLock");
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, dataPointer, bytes.Length);
            }
            finally
            {
                GlobalUnlock(globalHandle);
            }

            if (!OpenClipboard(ownerHandle))
            {
                error = GetLastWin32ErrorMessage("OpenClipboard");
                return false;
            }

            clipboardOpened = true;

            if (!EmptyClipboard())
            {
                error = GetLastWin32ErrorMessage("EmptyClipboard");
                return false;
            }

            if (SetClipboardData(CF_UNICODETEXT, globalHandle) == IntPtr.Zero)
            {
                error = GetLastWin32ErrorMessage("SetClipboardData");
                return false;
            }

            globalHandle = IntPtr.Zero;
            error = string.Empty;
            return true;
        }
        catch (ExternalException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (clipboardOpened)
                CloseClipboard();

            if (globalHandle != IntPtr.Zero)
                GlobalFree(globalHandle);
        }
    }

    private static string GetLastWin32ErrorMessage(string operation)
        => $"{operation} 失败，错误码：{Marshal.GetLastWin32Error()}";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
