using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Slave.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace SimulatorApp.Slave.ViewModels;

public partial class ModbusPacketCaptureViewModel : ObservableObject
{
    private const int MaxEntries = 5000;
    private const string AllSourcesFilter = "全部接口";

    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _statusText = "未开始";
    [ObservableProperty] private string _selectedSourceFilter = AllSourcesFilter;

    public ObservableCollection<ModbusPacketCaptureEntry> Entries { get; } = new();
    public ObservableCollection<string> SourceFilters { get; } = new() { AllSourcesFilter };
    public ICollectionView FilteredEntries { get; }
    public Func<bool> HasRunningListener { get; set; } = static () => false;

    public ModbusPacketCaptureViewModel()
    {
        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = EntryMatchesSourceFilter;
    }

    public bool TryStart()
    {
        if (!HasRunningListener())
        {
            MessageBox.Show("请先开启监听后再抓取报文。", "报文抓取", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        IsCapturing = true;
        IsPaused = false;
        StatusText = "抓取中";
        return true;
    }

    [RelayCommand]
    private void Start() => TryStart();

    [RelayCommand]
    private void Pause()
    {
        if (!IsCapturing)
            return;

        IsPaused = !IsPaused;
        StatusText = IsPaused ? "已暂停" : "抓取中";
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        SourceFilters.Clear();
        SourceFilters.Add(AllSourcesFilter);
        SelectedSourceFilter = AllSourcesFilter;
    }

    public void Append(ModbusPacketCaptureEntry entry)
    {
        if (!IsCapturing || IsPaused)
            return;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            while (Entries.Count >= MaxEntries)
                Entries.RemoveAt(0);

            AddSourceFilter(entry.Source);
            Entries.Add(entry);
        });
    }

    partial void OnSelectedSourceFilterChanged(string value)
        => FilteredEntries.Refresh();

    partial void OnIsCapturingChanged(bool value)
        => StatusText = value ? (IsPaused ? "已暂停" : "抓取中") : "未开始";

    partial void OnIsPausedChanged(bool value)
        => StatusText = IsCapturing ? (value ? "已暂停" : "抓取中") : "未开始";

    private bool EntryMatchesSourceFilter(object item)
    {
        if (item is not ModbusPacketCaptureEntry entry)
            return false;

        return string.Equals(SelectedSourceFilter, AllSourcesFilter, StringComparison.Ordinal)
            || string.Equals(entry.Source, SelectedSourceFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void AddSourceFilter(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (SourceFilters.Any(item => string.Equals(item, source, StringComparison.OrdinalIgnoreCase)))
            return;

        var insertIndex = 1;
        while (insertIndex < SourceFilters.Count
               && string.Compare(SourceFilters[insertIndex], source, StringComparison.OrdinalIgnoreCase) < 0)
        {
            insertIndex++;
        }

        SourceFilters.Insert(insertIndex, source);
    }
}
