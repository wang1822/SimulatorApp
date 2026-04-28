using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel
{
    private void ReadOnlyMetaEdit_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is null)
        {
            return;
        }

        var memberName = textBox.Tag as string;
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return;
        }

        var current = ReadStringMember(textBox.DataContext, memberName);
        textBox.Text = current ?? string.Empty;
    }

    private async void ReadOnlyMetaEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is null)
        {
            return;
        }

        var memberName = textBox.Tag as string;
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return;
        }

        var newValue = textBox.Text ?? string.Empty;
        var oldValue = ReadStringMember(textBox.DataContext, memberName) ?? string.Empty;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryWriteStringMember(textBox.DataContext, memberName, newValue))
        {
            return;
        }

        RaisePropertyChanged(textBox.DataContext, memberName);
        await PersistMetaEditAsync(textBox.DataContext).ConfigureAwait(true);
    }

    private string? ReadStringMember(object row, string memberName)
    {
        var type = row.GetType();

        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanRead == true && property.PropertyType == typeof(string))
        {
            return property.GetValue(row) as string;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.FieldType == typeof(string))
        {
            return field.GetValue(row) as string;
        }

        var backingField = type.GetField($"<{memberName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (backingField?.FieldType == typeof(string))
        {
            return backingField.GetValue(row) as string;
        }

        return null;
    }

    private bool TryWriteStringMember(object row, string memberName, string value)
    {
        var type = row.GetType();

        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = property?.GetSetMethod(true);
        if (setter is not null && property?.PropertyType == typeof(string))
        {
            setter.Invoke(row, new object?[] { value });
            return true;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is not null && field.FieldType == typeof(string))
        {
            field.SetValue(row, value);
            return true;
        }

        var backingField = type.GetField($"<{memberName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (backingField is not null && backingField.FieldType == typeof(string))
        {
            backingField.SetValue(row, value);
            return true;
        }

        return false;
    }

    private static void RaisePropertyChanged(object row, string propertyName)
    {
        var type = row.GetType();
        while (type is not null)
        {
            var method = type.GetMethod(
                "OnPropertyChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            if (method is not null)
            {
                method.Invoke(row, new object?[] { propertyName });
                return;
            }

            type = type.BaseType;
        }
    }

    private async Task PersistMetaEditAsync(object row)
    {
        try
        {
            var vm = DataContext;
            if (vm is null)
            {
                return;
            }

            var configId = ReadIntMember(vm, "DbId");
            if (configId <= 0)
            {
                return;
            }

            var dbService = ReadObjectMember(vm, "DbService");
            if (dbService is null)
            {
                return;
            }

            var address = ReadIntMember(row, "Address");
            if (address < 0)
            {
                return;
            }

            var chineseName = ReadStringMember(row, "ChineseName") ?? string.Empty;
            var englishName = ReadStringMember(row, "EnglishName") ?? string.Empty;
            var readWrite = ReadStringMember(row, "ReadWrite") ?? "R/W";
            var range = ReadStringMember(row, "Range") ?? string.Empty;
            var unit = ReadStringMember(row, "Unit") ?? string.Empty;
            var note = ReadStringMember(row, "Note") ?? string.Empty;
            var currentValueRaw = ReadUShortMember(row, "CurrentValueRaw");
            var sortOrder = ResolveSortOrder(vm, row);

            // Existing DB API does not expose Unit/Range/Note update.
            // Replace current row in DB to persist all metadata fields.
            var deleted = await TryInvokeTaskAsync(
                dbService,
                "DeleteRowAsync",
                configId,
                address).ConfigureAwait(true);

            if (!deleted)
            {
                return;
            }

            var inserted = await TryInvokeTaskAsync(
                dbService,
                "InsertRowAsync",
                configId,
                sortOrder,
                chineseName,
                englishName,
                address,
                readWrite,
                range,
                unit,
                note).ConfigureAwait(true);

            if (!inserted)
            {
                return;
            }

            await TryInvokeTaskAsync(
                dbService,
                "UpdateRowCurrentValueAsync",
                configId,
                address,
                currentValueRaw).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImportedDevicePanel] Persist meta edit failed: {ex}");
        }
    }

    private static int ResolveSortOrder(object vm, object row)
    {
        try
        {
            var rows = ReadObjectMember(vm, "Rows");
            if (rows is IList list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (ReferenceEquals(list[i], row))
                    {
                        return i;
                    }
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private static async Task<bool> TryInvokeTaskAsync(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
        {
            return false;
        }

        var result = method.Invoke(target, args);
        if (result is not Task task)
        {
            return false;
        }

        await task.ConfigureAwait(false);
        return true;
    }

    private static object? ReadObjectMember(object target, string memberName)
    {
        var type = target.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanRead == true)
        {
            return property.GetValue(target);
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is not null)
        {
            return field.GetValue(target);
        }

        var backingField = type.GetField($"<{memberName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        return backingField?.GetValue(target);
    }

    private static int ReadIntMember(object target, string memberName)
    {
        var value = ReadObjectMember(target, memberName);
        return value switch
        {
            int i => i,
            byte b => b,
            short s => s,
            _ => 0
        };
    }

    private static ushort ReadUShortMember(object target, string memberName)
    {
        var value = ReadObjectMember(target, memberName);
        return value switch
        {
            ushort us => us,
            short s when s >= 0 => (ushort)s,
            int i when i >= 0 && i <= ushort.MaxValue => (ushort)i,
            _ => 0
        };
    }
}
