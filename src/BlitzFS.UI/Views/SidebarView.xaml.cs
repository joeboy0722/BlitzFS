using System;
using System.Windows;
using System.Windows.Controls;

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
            if (sender is Button btn && btn.Tag is string targetPath)
            {
                PathSelected?.Invoke(this, targetPath);
            }
        }
    }
}
