using System;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel : UserControl
{
    private bool _isUpdatingSelectAll;

    private void SelectAllCheckBox_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshSelectAllCheckBoxState();
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAll)
        {
            return;
        }

        if (sender is not CheckBox checkBox)
        {
            return;
        }

        var targetChecked = checkBox.IsChecked == true;
        ApplyCheckedStateToRows(targetChecked);
        RefreshSelectAllCheckBoxState();
    }

    private void ApplyCheckedStateToRows(bool value)
    {
        var rows = GetRowsCollection();
        if (rows is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            SetBooleanProperty(row, "IsChecked", value);
        }
    }

    private IList? GetRowsCollection()
    {
        var vm = DataContext;
        if (vm is null)
        {
            return null;
        }

        return GetListProperty(vm, "FilteredRows") ?? GetListProperty(vm, "Rows");
    }

    private static IList? GetListProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance) as IList;
    }

    private void RefreshSelectAllCheckBoxState()
    {
        if (SelectAllCheckBox is null)
        {
            return;
        }

        var rows = GetRowsCollection();
        if (rows is null || rows.Count == 0)
        {
            _isUpdatingSelectAll = true;
            SelectAllCheckBox.IsChecked = false;
            _isUpdatingSelectAll = false;
            return;
        }

        var allChecked = true;
        foreach (var row in rows)
        {
            if (!GetBooleanProperty(row, "IsChecked"))
            {
                allChecked = false;
                break;
            }
        }

        _isUpdatingSelectAll = true;
        SelectAllCheckBox.IsChecked = allChecked;
        _isUpdatingSelectAll = false;
    }

    private static void SetBooleanProperty(object? target, string propertyName, bool value)
    {
        if (target is null)
        {
            return;
        }

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite != true)
        {
            return;
        }

        if (property.PropertyType == typeof(bool))
        {
            property.SetValue(target, value);
            return;
        }

        if (property.PropertyType == typeof(bool?))
        {
            property.SetValue(target, (bool?)value);
        }
    }

    private static bool GetBooleanProperty(object? target, string propertyName)
    {
        if (target is null)
        {
            return false;
        }

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead)
        {
            return false;
        }

        var raw = property.GetValue(target);
        if (raw is bool b)
        {
            return b;
        }

        return false;
    }
}
