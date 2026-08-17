using System;
using System.Windows;
using System.Windows.Controls;
using BlitzFS.UI.ViewModels;

namespace BlitzFS.UI.Views
{
    public partial class SidebarView : UserControl
    {
        public event EventHandler<string>? PathSelected;

        public SidebarView()
        {
            InitializeComponent();
        }

        private void OnSidebarItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string targetPath && !string.IsNullOrEmpty(targetPath))
            {
                PathSelected?.Invoke(this, targetPath);
            }
        }

        private void OnSidebarItemContextOpenClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is SidebarItemViewModel item && !string.IsNullOrEmpty(item.Path))
            {
                PathSelected?.Invoke(this, item.Path);
            }
        }

        private void OnSidebarItemCopyPathClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is SidebarItemViewModel item && !string.IsNullOrEmpty(item.Path))
            {
                try
                {
                    Clipboard.SetText(item.Path);
                }
                catch {}
            }
        }

        private void OnUnpinQuickAccessClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is SidebarItemViewModel item)
            {
                if (DataContext is SidebarViewModel vm)
                {
                    vm.UnpinFromQuickAccess(item.Path);
                }
            }
        }

        private void OnQuickUnpinButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button btn && btn.Tag is string path)
            {
                if (DataContext is SidebarViewModel vm)
                {
                    vm.UnpinFromQuickAccess(path);
                }
            }
        }

        private void OnEjectButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button btn && btn.Tag is string letter)
            {
                if (DataContext is SidebarViewModel vm)
                {
                    vm.EjectDrive(letter);
                }
            }
        }

        private void OnContextEjectDriveClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is SidebarItemViewModel item)
            {
                if (DataContext is SidebarViewModel vm)
                {
                    vm.EjectDrive(item.DriveLetter);
                }
            }
        }
    }
}
