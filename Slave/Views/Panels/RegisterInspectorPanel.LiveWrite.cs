using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.ViewModels;

namespace SimulatorApp.Slave.Views.Panels;

public partial class RegisterInspectorPanel
{
    private readonly HashSet<TextBox> _internalValueSyncTextBoxes = new();
    private readonly Dictionary<TextBox, DispatcherTimer> _debounceTimers = new();
    private readonly Dictionary<int, HashSet<TextBox>> _currentValueTextBoxesByAddress = new();
    private readonly ConditionalWeakTable<object, object> _defaultedCurrentValueDraftRows = new();
    private int _lastPasteAnchorRowIndex = -1;
    private int _lastPasteAnchorDisplayIndex = -1;
    private static readonly TimeSpan WriteDebounceInterval = TimeSpan.FromMilliseconds(220);
    private static readonly object CurrentValueDefaultedMarker = new();
    private bool _isCtrlDragSelecting;
    private bool _ctrlDragAddsSelection = true;
    private int _ctrlDragAnchorRowIndex = -1;
    private int _ctrlDragAnchorDisplayIndex = -1;

    private void CurrentValueTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        TrackCurrentValueTextBox(textBox);
        SyncCurrentValueText(textBox);
        if (textBox.IsKeyboardFocusWithin || textBox.IsFocused)
        {
            QueueMoveTextBoxCaretToEnd(textBox);
        }
    }

    private void CurrentValueTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            UpdatePasteAnchorFromElement(textBox);
            MoveTextBoxCaretToEnd(textBox);
        }
    }

    private void EditableCellTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            UpdatePasteAnchorFromElement(textBox);
            MoveTextBoxCaretToEnd(textBox);
            return;
        }

        if (sender is DependencyObject element)
        {
            UpdatePasteAnchorFromElement(element);
        }
    }

    private void EditableCellTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        UpdatePasteAnchorFromElement(textBox);
    }

    private void DataGridCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridCell cell ||
            InlineProtocolDraftGrid is null ||
            cell.Column is null ||
            !TryGetEditableField(cell.Column, out _))
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            StartCtrlDragSelection(cell);
            return;
        }

        // Shift 选择交给 DataGrid 默认逻辑，但先提交当前编辑，避免编辑框截获选择动作。
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            CommitInlineGridEdit();
            return;
        }

        if (cell.IsEditing)
        {
            return;
        }

        e.Handled = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            BeginEditCellFromSingleClick(cell);
        }), DispatcherPriority.Input);
    }

    private void StartCtrlDragSelection(DataGridCell cell)
    {
        if (InlineProtocolDraftGrid is null || !TryGetCellPosition(cell, out var rowIndex, out var displayIndex))
        {
            return;
        }

        CommitInlineGridEdit();
        InlineProtocolDraftGrid.Focus();

        var info = new DataGridCellInfo(cell);
        if (!info.IsValid)
        {
            return;
        }

        _isCtrlDragSelecting = true;
        _ctrlDragAnchorRowIndex = rowIndex;
        _ctrlDragAnchorDisplayIndex = displayIndex;
        _ctrlDragAddsSelection = !InlineProtocolDraftGrid.SelectedCells.Contains(info);

        ApplyCtrlSelectionRange(rowIndex, displayIndex);
        InlineProtocolDraftGrid.CurrentCell = info;
        cell.Focus();
        UpdatePasteAnchorFromElement(cell);
        Mouse.Capture(InlineProtocolDraftGrid, CaptureMode.SubTree);
    }

    private void InlineProtocolDraftGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isCtrlDragSelecting ||
            InlineProtocolDraftGrid is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            FinishCtrlDragSelection();
            return;
        }

        if (FindParent<DataGridCell>(e.OriginalSource as DependencyObject) is not { } cell ||
            cell.Column is null ||
            !TryGetEditableField(cell.Column, out _))
        {
            return;
        }

        if (!TryGetCellPosition(cell, out var rowIndex, out var displayIndex))
        {
            return;
        }

        e.Handled = true;
        ApplyCtrlSelectionRange(rowIndex, displayIndex);
        InlineProtocolDraftGrid.CurrentCell = new DataGridCellInfo(cell);
        UpdatePasteAnchorFromElement(cell);
    }

    private void InlineProtocolDraftGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCtrlDragSelecting)
        {
            return;
        }

        e.Handled = true;
        FinishCtrlDragSelection();
    }

    private void InlineProtocolDraftGrid_LostMouseCapture(object sender, MouseEventArgs e)
    {
        FinishCtrlDragSelection();
    }

    private void FinishCtrlDragSelection()
    {
        if (!_isCtrlDragSelecting)
        {
            return;
        }

        _isCtrlDragSelecting = false;
        _ctrlDragAnchorRowIndex = -1;
        _ctrlDragAnchorDisplayIndex = -1;

        if (Mouse.Captured == InlineProtocolDraftGrid)
        {
            Mouse.Capture(null);
        }
    }

    private void ApplyCtrlSelectionRange(int rowIndex, int displayIndex)
    {
        if (InlineProtocolDraftGrid is null ||
            _ctrlDragAnchorRowIndex < 0 ||
            _ctrlDragAnchorDisplayIndex < 0)
        {
            return;
        }

        var firstRow = Math.Min(_ctrlDragAnchorRowIndex, rowIndex);
        var lastRow = Math.Max(_ctrlDragAnchorRowIndex, rowIndex);
        var firstDisplay = Math.Min(_ctrlDragAnchorDisplayIndex, displayIndex);
        var lastDisplay = Math.Max(_ctrlDragAnchorDisplayIndex, displayIndex);
        var columns = InlineProtocolDraftGrid.Columns
            .Where(c => c.DisplayIndex >= firstDisplay && c.DisplayIndex <= lastDisplay && TryGetEditableField(c, out _))
            .ToList();

        for (var i = firstRow; i <= lastRow; i++)
        {
            if (InlineProtocolDraftGrid.Items[i] is not InlineProtocolDraftRow row)
            {
                continue;
            }

            foreach (var column in columns)
            {
                var info = new DataGridCellInfo(row, column);
                if (!info.IsValid)
                {
                    continue;
                }

                if (_ctrlDragAddsSelection)
                {
                    if (!InlineProtocolDraftGrid.SelectedCells.Contains(info))
                    {
                        InlineProtocolDraftGrid.SelectedCells.Add(info);
                    }
                }
                else
                {
                    if (InlineProtocolDraftGrid.SelectedCells.Contains(info))
                    {
                        InlineProtocolDraftGrid.SelectedCells.Remove(info);
                    }
                }
            }
        }
    }

    private bool TryGetCellPosition(DataGridCell cell, out int rowIndex, out int displayIndex)
    {
        rowIndex = -1;
        displayIndex = -1;

        if (InlineProtocolDraftGrid is null ||
            cell.DataContext is not InlineProtocolDraftRow row ||
            cell.Column is null ||
            !TryGetEditableField(cell.Column, out _))
        {
            return false;
        }

        rowIndex = InlineProtocolDraftGrid.Items.IndexOf(row);
        displayIndex = cell.Column.DisplayIndex;
        return rowIndex >= 0 && displayIndex >= 0;
    }

    private void CommitInlineGridEdit()
    {
        InlineProtocolDraftGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
        InlineProtocolDraftGrid?.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void BeginEditCellFromSingleClick(DataGridCell cell)
    {
        if (InlineProtocolDraftGrid is null ||
            cell.IsEditing ||
            cell.Column is null ||
            !TryGetEditableField(cell.Column, out _))
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
        {
            return;
        }

        InlineProtocolDraftGrid.Focus();
        InlineProtocolDraftGrid.SelectedCells.Clear();
        cell.IsSelected = true;
        InlineProtocolDraftGrid.CurrentCell = new DataGridCellInfo(cell);
        cell.Focus();

        if (!InlineProtocolDraftGrid.BeginEdit())
        {
            return;
        }

        QueueFocusCellEditor(cell);
    }

    private void QueueFocusCellEditor(DataGridCell cell, int attempt = 0)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (InlineProtocolDraftGrid is null)
            {
                return;
            }

            cell.UpdateLayout();
            if (FindVisualChild<TextBox>(cell) is TextBox editor)
            {
                editor.Focus();
                Keyboard.Focus(editor);
                MoveTextBoxCaretToEnd(editor);
                return;
            }

            if (attempt < 3)
            {
                QueueFocusCellEditor(cell, attempt + 1);
            }
        }), attempt == 0 ? DispatcherPriority.Loaded : DispatcherPriority.ContextIdle);
    }

    private static void MoveTextBoxCaretToEnd(TextBox textBox)
    {
        var textLength = textBox.Text?.Length ?? 0;
        textBox.CaretIndex = textLength;
        textBox.SelectionStart = textLength;
        textBox.SelectionLength = 0;
    }

    private void QueueMoveTextBoxCaretToEnd(TextBox textBox)
    {
        Dispatcher.BeginInvoke(
            new Action(() => MoveTextBoxCaretToEnd(textBox)),
            DispatcherPriority.Background);
    }

    private void CurrentValueTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        StopDebounce(textBox);
        TrySendCurrentValue(textBox, showFormatError: true);
    }

    private void CurrentValueTextBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            RemoveDebounceTimer(textBox);
            UntrackCurrentValueTextBox(textBox);
        }
    }

    private void CurrentValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || _internalValueSyncTextBoxes.Contains(textBox))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            return;
        }

        QueueDebouncedWrite(textBox);
    }

    private void QueueDebouncedWrite(TextBox textBox)
    {
        if (!_debounceTimers.TryGetValue(textBox, out var timer))
        {
            timer = new DispatcherTimer { Interval = WriteDebounceInterval, Tag = textBox };
            timer.Tick += DebounceWriteTimer_Tick;
            _debounceTimers[textBox] = timer;
        }

        timer.Stop();
        timer.Start();
    }

    private void DebounceWriteTimer_Tick(object? sender, EventArgs e)
    {
        if (sender is not DispatcherTimer timer || timer.Tag is not TextBox textBox)
        {
            return;
        }

        timer.Stop();
        TrySendCurrentValue(textBox, showFormatError: false);
    }

    private void StopDebounce(TextBox textBox)
    {
        if (_debounceTimers.TryGetValue(textBox, out var timer))
        {
            timer.Stop();
        }
    }

    private void RemoveDebounceTimer(TextBox textBox)
    {
        if (!_debounceTimers.TryGetValue(textBox, out var timer))
        {
            return;
        }

        timer.Stop();
        timer.Tick -= DebounceWriteTimer_Tick;
        _debounceTimers.Remove(textBox);
    }

    private void TrySendCurrentValue(TextBox textBox, bool showFormatError)
    {
        if (!TryGetDraftAddress(textBox.DataContext, out var address))
        {
            return;
        }

        if (DataContext is not RegisterInspectorViewModel vm)
        {
            if (showFormatError)
            {
                SetInlineWriteStatus("未找到寄存器检视上下文，无法写入。", false);
            }
            return;
        }

        TrySendCurrentValueByAddress(
            vm,
            address,
            textBox.Text,
            showFormatError,
            emitStatus: true,
            out _);
    }

    private enum DraftEditableField
    {
        ChineseName,
        EnglishName,
        CurrentValue,
        Unit,
        Range,
        Note
    }

    private void PasteDraftFields_Click(object sender, RoutedEventArgs e)
    {
        TryPasteDraftFieldsFromClipboard();
    }

    private void InlineProtocolDraftGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCtrlKey(e.Key))
        {
            ExitInlineEditForSelectionMode();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control || e.Key != Key.V)
        {
            return;
        }

        e.Handled = TryPasteDraftFieldsFromClipboard();
    }

    private void ExitInlineEditForSelectionMode()
    {
        if (InlineProtocolDraftGrid is null)
        {
            return;
        }

        CommitInlineGridEdit();
        InlineProtocolDraftGrid.SelectedCells.Clear();
        InlineProtocolDraftGrid.CurrentCell = default;
        InlineProtocolDraftGrid.Focus();
        Keyboard.Focus(InlineProtocolDraftGrid);
    }

    private static bool IsCtrlKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl;

    private bool TryPasteDraftFieldsFromClipboard()
    {
        if (InlineProtocolDraftGrid is null)
        {
            return false;
        }

        if (DataContext is not RegisterInspectorViewModel vm)
        {
            SetInlineWriteStatus("未找到寄存器检视上下文，无法粘贴。", false);
            return true;
        }

        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText())
            {
                SetInlineWriteStatus("剪贴板没有可粘贴的文本。", false);
                return true;
            }

            clipboardText = GetClipboardTextForMatrixPaste();
        }
        catch (Exception ex)
        {
            SetInlineWriteStatus($"读取剪贴板失败：{ex.Message}", false);
            return true;
        }

        var selectedTargets = GetSelectedEditableTargets();
        var clipboardMatrix = ParseClipboardMatrix(clipboardText);
        if (clipboardMatrix.Count == 0)
        {
            SetInlineWriteStatus("剪贴板内容为空，未执行粘贴。", false);
            return true;
        }

        var appliedCount = 0;
        var failedMessages = new List<string>();

        if (clipboardMatrix.Count == 1 && clipboardMatrix[0].Count == 1 && selectedTargets.Count > 0)
        {
            var singleValue = clipboardMatrix[0][0];
            foreach (var target in selectedTargets)
            {
                if (TryApplyDraftFieldValue(vm, target.Row, target.Field, singleValue, out var message))
                {
                    appliedCount++;
                }
                else if (!string.IsNullOrWhiteSpace(message))
                {
                    failedMessages.Add(message);
                }
            }
        }
        else
        {
            if (!TryGetPasteAnchor(out var startRowIndex, out var startDisplayColumnIndex))
            {
                SetInlineWriteStatus("请先选中需要粘贴的字段单元格。", false);
                return true;
            }

            for (var rowOffset = 0; rowOffset < clipboardMatrix.Count; rowOffset++)
            {
                var rowIndex = startRowIndex + rowOffset;
                if (!TryGetDraftRowAt(rowIndex, out var row))
                {
                    continue;
                }

                var rowValues = clipboardMatrix[rowOffset];
                for (var colOffset = 0; colOffset < rowValues.Count; colOffset++)
                {
                    var displayColumnIndex = startDisplayColumnIndex + colOffset;
                    var column = FindColumnByDisplayIndex(displayColumnIndex);
                    if (column is null || !TryGetEditableField(column, out var field))
                    {
                        continue;
                    }

                    if (TryApplyDraftFieldValue(vm, row, field, rowValues[colOffset], out var message))
                    {
                        appliedCount++;
                    }
                    else if (!string.IsNullOrWhiteSpace(message))
                    {
                        failedMessages.Add(message);
                    }
                }
            }
        }

        if (appliedCount <= 0)
        {
            SetInlineWriteStatus(
                failedMessages.Count > 0
                    ? $"粘贴失败：{failedMessages[0]}"
                    : "未命中可写字段，未执行粘贴。",
                false);
            return true;
        }

        RefreshInlineDraftGridVisuals();

        if (failedMessages.Count > 0)
        {
            SetInlineWriteStatus(
                $"已粘贴 {appliedCount} 个字段，{failedMessages.Count} 个失败。首个失败：{failedMessages[0]}",
                false);
            return true;
        }

        SetInlineWriteStatus($"已粘贴 {appliedCount} 个字段。", true);
        return true;
    }

    private void RefreshInlineDraftGridVisuals()
    {
        if (InlineProtocolDraftGrid is null)
        {
            return;
        }

        try
        {
            InlineProtocolDraftGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            InlineProtocolDraftGrid.CommitEdit(DataGridEditingUnit.Row, true);
            InlineProtocolDraftGrid.Items.Refresh();
            InlineProtocolDraftGrid.UpdateLayout();
        }
        catch
        {
            // ignore refresh glitches
        }
    }

    private static string GetClipboardTextForMatrixPaste()
    {
        // Excel 的普通文本格式有时会把“单元格内换行”暴露成普通换行；CSV 格式会用引号保留它。
        if (Clipboard.ContainsData(DataFormats.CommaSeparatedValue) &&
            Clipboard.GetData(DataFormats.CommaSeparatedValue) is string csvText &&
            !string.IsNullOrEmpty(csvText))
        {
            return csvText;
        }

        return Clipboard.GetText();
    }

    private static List<List<string>> ParseClipboardMatrix(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return new List<List<string>>();
        }

        var delimiter = DetectClipboardDelimiter(rawText);
        var result = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < rawText.Length; i++)
        {
            var ch = rawText[i];

            if (ch == '"')
            {
                if (!inQuotes)
                {
                    if (field.Length == 0)
                    {
                        inQuotes = true;
                        continue;
                    }

                    field.Append(ch);
                    continue;
                }

                if (i + 1 < rawText.Length && rawText[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                inQuotes = false;
                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if ((ch == '\r' || ch == '\n') && !inQuotes)
            {
                row.Add(field.ToString());
                field.Clear();
                result.Add(row);
                row = new List<string>();

                if (ch == '\r' && i + 1 < rawText.Length && rawText[i + 1] == '\n')
                {
                    i++;
                }
                continue;
            }

            field.Append(ch);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            result.Add(row);
        }

        // 去掉由末尾换行产生的空白行。
        if (result.Count > 0 &&
            result[^1].Count == 1 &&
            string.IsNullOrEmpty(result[^1][0]) &&
            (rawText.EndsWith('\n') || rawText.EndsWith('\r')))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static char DetectClipboardDelimiter(string rawText)
    {
        var tabCount = 0;
        var commaCount = 0;
        var inQuotes = false;

        for (var i = 0; i < rawText.Length; i++)
        {
            var ch = rawText[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < rawText.Length && rawText[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (ch == '	')
            {
                tabCount++;
            }
            else if (ch == ',')
            {
                commaCount++;
            }
        }

        return tabCount > 0 ? '	' : commaCount > 0 ? ',' : '	';
    }

    private List<(InlineProtocolDraftRow Row, DraftEditableField Field)> GetSelectedEditableTargets()
    {
        var results = new List<(InlineProtocolDraftRow Row, DraftEditableField Field)>();
        if (InlineProtocolDraftGrid is null)
        {
            return results;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in InlineProtocolDraftGrid.SelectedCells)
        {
            if (cell.Item is not InlineProtocolDraftRow row || cell.Column is null)
            {
                continue;
            }

            if (!TryGetEditableField(cell.Column, out var field))
            {
                continue;
            }

            var rowIndex = InlineProtocolDraftGrid.Items.IndexOf(cell.Item);
            if (rowIndex < 0)
            {
                continue;
            }

            var key = $"{rowIndex}:{cell.Column.DisplayIndex}";
            if (!seen.Add(key))
            {
                continue;
            }

            results.Add((row, field));
        }

        return results;
    }

    private bool TryGetPasteAnchor(out int rowIndex, out int displayColumnIndex)
    {
        rowIndex = -1;
        displayColumnIndex = -1;

        if (InlineProtocolDraftGrid is null)
        {
            return false;
        }

        var selectedCells = InlineProtocolDraftGrid.SelectedCells
            .Where(c => c.Item is InlineProtocolDraftRow && c.Column is not null && TryGetEditableField(c.Column, out _))
            .ToList();

        // 多选时，锚点固定为选区左上角，避免焦点落在末行导致整体下移一行粘贴。
        if (selectedCells.Count > 1)
        {
            var selectedTopLeft = selectedCells
                .OrderBy(c => InlineProtocolDraftGrid.Items.IndexOf(c.Item))
                .ThenBy(c => c.Column!.DisplayIndex)
                .First();
            rowIndex = InlineProtocolDraftGrid.Items.IndexOf(selectedTopLeft.Item);
            displayColumnIndex = selectedTopLeft.Column!.DisplayIndex;
            if (rowIndex >= 0 && displayColumnIndex >= 0)
            {
                return true;
            }
        }

        if (TryGetFocusedEditableCell(out var focusedRow, out var focusedColumn))
        {
            rowIndex = InlineProtocolDraftGrid.Items.IndexOf(focusedRow);
            displayColumnIndex = focusedColumn.DisplayIndex;
            return rowIndex >= 0 && displayColumnIndex >= 0;
        }

        if (selectedCells.Count == 1)
        {
            rowIndex = InlineProtocolDraftGrid.Items.IndexOf(selectedCells[0].Item);
            displayColumnIndex = selectedCells[0].Column!.DisplayIndex;
            if (rowIndex >= 0 && displayColumnIndex >= 0)
            {
                return true;
            }
        }

        if (TryGetLastPasteAnchor(out rowIndex, out displayColumnIndex))
        {
            return true;
        }

        var currentCell = InlineProtocolDraftGrid.CurrentCell;
        if (currentCell.Item is InlineProtocolDraftRow currentRow && currentCell.Column is not null)
        {
            rowIndex = InlineProtocolDraftGrid.Items.IndexOf(currentRow);
            if (rowIndex >= 0)
            {
                displayColumnIndex = currentCell.Column.DisplayIndex;
                if (!TryGetEditableField(currentCell.Column, out _))
                {
                    displayColumnIndex = FindFirstEditableDisplayIndex(displayColumnIndex);
                }

                if (displayColumnIndex >= 0)
                {
                    return true;
                }
            }
        }

        if (InlineProtocolDraftGrid.Items.Count > 0)
        {
            rowIndex = 0;
            displayColumnIndex = FindFirstEditableDisplayIndex(0);
            return displayColumnIndex >= 0;
        }

        return false;
    }

    private bool TryGetFocusedEditableCell(out InlineProtocolDraftRow row, out DataGridColumn column)
    {
        row = null!;
        column = null!;

        if (InlineProtocolDraftGrid is null)
        {
            return false;
        }

        var focusedObject = Keyboard.FocusedElement as DependencyObject;
        var cell = FindParent<DataGridCell>(focusedObject);
        if (cell?.DataContext is not InlineProtocolDraftRow focusedRow)
        {
            return false;
        }

        if (cell.Column is null || !TryGetEditableField(cell.Column, out _))
        {
            return false;
        }

        row = focusedRow;
        column = cell.Column;
        return true;
    }

    private bool TryGetLastPasteAnchor(out int rowIndex, out int displayColumnIndex)
    {
        rowIndex = _lastPasteAnchorRowIndex;
        displayColumnIndex = _lastPasteAnchorDisplayIndex;

        if (InlineProtocolDraftGrid is null)
        {
            return false;
        }

        if (rowIndex < 0 || rowIndex >= InlineProtocolDraftGrid.Items.Count || displayColumnIndex < 0)
        {
            return false;
        }

        var column = FindColumnByDisplayIndex(displayColumnIndex);
        return column is not null && TryGetEditableField(column, out _);
    }

    private void UpdatePasteAnchorFromElement(DependencyObject element)
    {
        if (InlineProtocolDraftGrid is null)
        {
            return;
        }

        var cell = FindParent<DataGridCell>(element);
        if (cell?.DataContext is not InlineProtocolDraftRow row || cell.Column is null)
        {
            return;
        }

        if (!TryGetEditableField(cell.Column, out _))
        {
            return;
        }

        var rowIndex = InlineProtocolDraftGrid.Items.IndexOf(row);
        if (rowIndex < 0)
        {
            return;
        }

        _lastPasteAnchorRowIndex = rowIndex;
        _lastPasteAnchorDisplayIndex = cell.Column.DisplayIndex;
    }

    private static T? FindParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T t)
            {
                return t;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T matched)
            {
                return matched;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private int FindFirstEditableDisplayIndex(int startDisplayIndex)
    {
        if (InlineProtocolDraftGrid is null)
        {
            return -1;
        }

        var editableAfterStart = InlineProtocolDraftGrid.Columns
            .Where(c => c.DisplayIndex >= startDisplayIndex && TryGetEditableField(c, out _))
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.DisplayIndex)
            .FirstOrDefault(-1);

        if (editableAfterStart >= 0)
        {
            return editableAfterStart;
        }

        return InlineProtocolDraftGrid.Columns
            .Where(c => TryGetEditableField(c, out _))
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.DisplayIndex)
            .FirstOrDefault(-1);
    }

    private DataGridColumn? FindColumnByDisplayIndex(int displayIndex)
    {
        return InlineProtocolDraftGrid?.Columns.FirstOrDefault(c => c.DisplayIndex == displayIndex);
    }

    private bool TryGetDraftRowAt(int rowIndex, out InlineProtocolDraftRow row)
    {
        row = null!;
        if (InlineProtocolDraftGrid is null || rowIndex < 0 || rowIndex >= InlineProtocolDraftGrid.Items.Count)
        {
            return false;
        }

        if (InlineProtocolDraftGrid.Items[rowIndex] is not InlineProtocolDraftRow draftRow)
        {
            return false;
        }

        row = draftRow;
        return true;
    }

    private static bool TryGetEditableField(DataGridColumn column, out DraftEditableField field)
    {
        field = default;
        var header = column.Header?.ToString()?.Trim().TrimEnd('*').Trim();
        return header switch
        {
            "中文名" => SetField(DraftEditableField.ChineseName, out field),
            "英文名" => SetField(DraftEditableField.EnglishName, out field),
            "当前值" => SetField(DraftEditableField.CurrentValue, out field),
            "单位" => SetField(DraftEditableField.Unit, out field),
            "范围" => SetField(DraftEditableField.Range, out field),
            "描述" => SetField(DraftEditableField.Note, out field),
            _ => false
        };

        static bool SetField(DraftEditableField value, out DraftEditableField output)
        {
            output = value;
            return true;
        }
    }

    private bool TryApplyDraftFieldValue(
        RegisterInspectorViewModel vm,
        InlineProtocolDraftRow row,
        DraftEditableField field,
        string rawValue,
        out string failureMessage)
    {
        failureMessage = string.Empty;
        switch (field)
        {
            case DraftEditableField.ChineseName:
                row.ChineseName = rawValue ?? string.Empty;
                return true;
            case DraftEditableField.EnglishName:
                row.EnglishName = rawValue ?? string.Empty;
                return true;
            case DraftEditableField.Unit:
                row.Unit = rawValue ?? string.Empty;
                return true;
            case DraftEditableField.Range:
                row.Range = rawValue ?? string.Empty;
                return true;
            case DraftEditableField.Note:
                row.Note = rawValue ?? string.Empty;
                return true;
            case DraftEditableField.CurrentValue:
                return TrySendCurrentValueByAddress(
                    vm,
                    row.Address,
                    rawValue,
                    showFormatError: false,
                    emitStatus: false,
                    out failureMessage);
            default:
                return false;
        }
    }

    private bool TrySendCurrentValueByAddress(
        RegisterInspectorViewModel vm,
        int address,
        string? rawInput,
        bool showFormatError,
        bool emitStatus,
        out string failureMessage)
    {
        failureMessage = string.Empty;

        var inspectorRow = vm.Rows.FirstOrDefault(r => r.Address == address);
        if (inspectorRow is null)
        {
            // 若存在草稿行但尚未建立对应检视行，自动补建后再发送。
            var oldAddress = vm.NewAddress;
            try
            {
                vm.NewAddress = address;
                if (vm.AddRowCommand.CanExecute(null))
                {
                    vm.AddRowCommand.Execute(null);
                }
            }
            finally
            {
                vm.NewAddress = oldAddress;
            }

            inspectorRow = vm.Rows.FirstOrDefault(r => r.Address == address);
            if (inspectorRow is null)
            {
                failureMessage = $"地址 {address} 未加载到检视列表，无法发送。";
                if (emitStatus)
                {
                    SetInlineWriteStatus(failureMessage, false);
                }
                return false;
            }
        }

        var raw = rawInput?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        if (!ushort.TryParse(raw, out var value))
        {
            failureMessage = $"地址 {address} 当前值必须是 0~65535 的十进制整数。";
            if (showFormatError && emitStatus)
            {
                SetInlineWriteStatus(failureMessage, false);
            }
            return false;
        }

        try
        {
            if (inspectorRow.Value != value)
            {
                inspectorRow.Value = value;
            }

            // 当前值一修改即实时写入并发送给从站寄存器，供工控机侧立即检视。
            inspectorRow.RequestWrite();

            // 双保险：直接写入 RegisterBank，并强制同步到运行中的监听服务 DataStore。
            var bank = TryGetRegisterBank(vm);
            bank?.Write(address, value);
            var runtimeSharedBank = TryGetRuntimeSharedBank();
            if (runtimeSharedBank is not null && !ReferenceEquals(runtimeSharedBank, bank))
            {
                runtimeSharedBank.Write(address, value);
            }

            SyncVisibleCurrentValueText(address, value);

            var syncedListeners = ForceSyncRunningListeners(address, value, out var syncDetail);
            var statusMessage = syncedListeners > 0
                ? $"地址 {address} 已实时发送当前值 {value}（0x{value:X4}），已同步 {syncedListeners} 个监听。"
                : $"地址 {address} 已实时写入当前值 {value}（0x{value:X4}），但未命中运行中监听。{syncDetail}";

            if (emitStatus)
            {
                SetInlineWriteStatus(statusMessage, syncedListeners > 0);
            }

            return true;
        }
        catch (Exception ex)
        {
            failureMessage = $"地址 {address} 实时发送失败：{ex.Message}";
            if (emitStatus)
            {
                SetInlineWriteStatus(failureMessage, false);
            }
            return false;
        }
    }

    private void TrackCurrentValueTextBox(TextBox textBox)
    {
        if (!TryGetDraftAddress(textBox.DataContext, out var address))
        {
            return;
        }

        if (!_currentValueTextBoxesByAddress.TryGetValue(address, out var textBoxes))
        {
            textBoxes = new HashSet<TextBox>();
            _currentValueTextBoxesByAddress[address] = textBoxes;
        }

        textBoxes.Add(textBox);
    }

    private void UntrackCurrentValueTextBox(TextBox textBox)
    {
        if (!TryGetDraftAddress(textBox.DataContext, out var address))
        {
            return;
        }

        if (!_currentValueTextBoxesByAddress.TryGetValue(address, out var textBoxes))
        {
            return;
        }

        textBoxes.Remove(textBox);
        if (textBoxes.Count == 0)
        {
            _currentValueTextBoxesByAddress.Remove(address);
        }
    }

    private void SyncVisibleCurrentValueText(int address, ushort value)
    {
        if (!_currentValueTextBoxesByAddress.TryGetValue(address, out var textBoxes) || textBoxes.Count == 0)
        {
            return;
        }

        foreach (var textBox in textBoxes.ToList())
        {
            if (textBox is null)
            {
                continue;
            }

            _internalValueSyncTextBoxes.Add(textBox);
            try
            {
                textBox.Text = value.ToString();
            }
            finally
            {
                _internalValueSyncTextBoxes.Remove(textBox);
            }
        }
    }

    private void SyncCurrentValueText(TextBox textBox)
    {
        if (!TryGetDraftAddress(textBox.DataContext, out var address))
        {
            SetCurrentValueTextInternal(textBox, "0");
            return;
        }

        if (DataContext is not RegisterInspectorViewModel vm)
        {
            SetCurrentValueTextInternal(textBox, "0");
            return;
        }

        var inspectorRow = vm.Rows.FirstOrDefault(r => r.Address == address);
        if (inspectorRow is null)
        {
            SetCurrentValueTextInternal(textBox, "0");
            return;
        }

        if (textBox.DataContext is { } draftRowContext &&
            TryMarkDraftRowCurrentValueDefaulted(draftRowContext))
        {
            InitializeInspectorCurrentValueToZero(vm, address, inspectorRow);
            SetCurrentValueTextInternal(textBox, "0");
            return;
        }

        SetCurrentValueTextInternal(textBox, inspectorRow.Value.ToString());
    }

    private bool TryMarkDraftRowCurrentValueDefaulted(object draftRowContext)
    {
        if (_defaultedCurrentValueDraftRows.TryGetValue(draftRowContext, out _))
        {
            return false;
        }

        _defaultedCurrentValueDraftRows.Add(draftRowContext, CurrentValueDefaultedMarker);
        return true;
    }

    private void InitializeInspectorCurrentValueToZero(
        RegisterInspectorViewModel vm,
        int address,
        dynamic inspectorRow)
    {
        try
        {
            if (inspectorRow.Value != 0)
            {
                inspectorRow.Value = 0;
            }
        }
        catch
        {
            // ignore row assignment errors
        }

        try
        {
            var bank = TryGetRegisterBank(vm);
            bank?.Write(address, 0);

            var runtimeSharedBank = TryGetRuntimeSharedBank();
            if (runtimeSharedBank is not null && !ReferenceEquals(runtimeSharedBank, bank))
            {
                runtimeSharedBank.Write(address, 0);
            }
        }
        catch
        {
            // ignore bank sync errors
        }

        SyncVisibleCurrentValueText(address, 0);
    }

    private void SetCurrentValueTextInternal(TextBox textBox, string text)
    {
        var shouldRestoreCaret = textBox.IsKeyboardFocusWithin || textBox.IsFocused;
        var caretIndex = textBox.CaretIndex;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        _internalValueSyncTextBoxes.Add(textBox);
        try
        {
            textBox.Text = text;
        }
        finally
        {
            _internalValueSyncTextBoxes.Remove(textBox);
        }

        if (shouldRestoreCaret)
        {
            QueueRestoreTextBoxSelection(textBox, caretIndex, selectionStart, selectionLength);
        }
    }

    private void QueueRestoreTextBoxSelection(TextBox textBox, int caretIndex, int selectionStart, int selectionLength)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var textLength = textBox.Text?.Length ?? 0;
            var safeSelectionStart = Math.Clamp(selectionStart, 0, textLength);
            var safeSelectionLength = Math.Clamp(selectionLength, 0, textLength - safeSelectionStart);
            textBox.SelectionStart = safeSelectionStart;
            textBox.SelectionLength = safeSelectionLength;
            textBox.CaretIndex = Math.Clamp(caretIndex, 0, textLength);
        }), DispatcherPriority.Background);
    }

    private static bool TryGetDraftAddress(object? dataContext, out int address)
    {
        address = 0;
        if (dataContext is null)
        {
            return false;
        }

        var prop = dataContext.GetType().GetProperty("Address");
        if (prop?.GetValue(dataContext) is int intAddress)
        {
            address = intAddress;
            return true;
        }

        return false;
    }

    private static RegisterBank? TryGetRegisterBank(RegisterInspectorViewModel vm)
    {
        var field = typeof(RegisterInspectorViewModel)
            .GetField("_bank", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(vm) as RegisterBank;
    }

    private static int ForceSyncRunningListeners(int address, ushort value, out string detail)
    {
        detail = string.Empty;
        try
        {
            var slaveVm = TryGetRuntimeSlaveViewModel();
            if (slaveVm is null)
            {
                detail = "运行时未获取到从站上下文。";
                return 0;
            }

            var listeners = slaveVm.Listeners;
            var syncedCount = 0;

            foreach (var listener in listeners)
            {
                if (!listener.IsRunning)
                {
                    continue;
                }

                var listenerType = listener.GetType();
                var serviceProp = listenerType.GetProperty(
                    "Service",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var service = serviceProp?.GetValue(listener);
                if (service is null)
                {
                    continue;
                }

                var syncMethod = service.GetType().GetMethod(
                    "SyncOneRegister",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(int), typeof(ushort) },
                    modifiers: null);

                if (syncMethod is null)
                {
                    continue;
                }

                syncMethod.Invoke(service, new object[] { address, value });
                syncedCount++;
            }

            if (syncedCount == 0)
            {
                detail = "当前无运行中监听。";
            }

            return syncedCount;
        }
        catch (Exception ex)
        {
            detail = $"同步监听异常：{ex.Message}";
            return 0;
        }
    }

    private static SlaveViewModel? TryGetRuntimeSlaveViewModel()
    {
        try
        {
            var services = App.Services;
            var fromService = services?.GetService(typeof(SlaveViewModel)) as SlaveViewModel;
            if (fromService is not null)
            {
                return fromService;
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            if (Application.Current?.MainWindow?.DataContext is SlaveViewModel mainWindowVm)
            {
                return mainWindowVm;
            }

            if (Application.Current is not null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.DataContext is SlaveViewModel vm)
                    {
                        return vm;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static RegisterBank? TryGetRuntimeSharedBank()
    {
        var slaveVm = TryGetRuntimeSlaveViewModel();
        if (slaveVm is null)
        {
            return null;
        }

        try
        {
            var field = typeof(SlaveViewModel)
                .GetField("_bank", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(slaveVm) as RegisterBank;
        }
        catch
        {
            return null;
        }
    }

    private void SetInlineWriteStatus(string message, bool isSuccess)
    {
        if (InlineWriteStatusTextBlock is null)
        {
            return;
        }

        InlineWriteStatusTextBlock.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isSuccess ? "#86EFAC" : "#FCA5A5"));
        InlineWriteStatusTextBlock.Text = message;
    }
}
