using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.ViewModels;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel
{
    private void EditProtocol_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthService.Current.IsAdmin)
        {
            MessageBox.Show(Window.GetWindow(this), "普通用户不能编辑协议。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DataContext is not ImportedDeviceViewModel imported)
            return;

        var slaveVm = FindAncestorDataContext<SlaveViewModel>(this);
        if (slaveVm is null)
        {
            MessageBox.Show("未找到从站主上下文，无法编辑协议。", "编辑协议", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        slaveVm.BeginEditImportedProtocol(imported);
    }

    private static TContext? FindAncestorDataContext<TContext>(DependencyObject? start)
        where TContext : class
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is TContext context)
                return context;

            current = VisualTreeHelper.GetParent(current);
        }

        if (Application.Current is not null)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext is TContext directContext)
                    return directContext;

                var dataContext = window.DataContext;
                if (dataContext is null)
                    continue;

                var slaveVmProperty = dataContext.GetType().GetProperty("SlaveVm", BindingFlags.Public | BindingFlags.Instance);
                if (slaveVmProperty?.GetValue(dataContext) is TContext nestedContext)
                    return nestedContext;
            }
        }

        return null;
    }
}
