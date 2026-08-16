using System;
using System.Windows;

namespace BlitzFS.UI
{
    /// <summary>
    /// App.xaml 的互動邏輯 (含全域未處理例外保護)
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[UI Exception] {args.Exception}");
                MessageBox.Show($"發生非預期錯誤: {args.Exception.Message}\n{args.Exception.StackTrace}", "BlitzFS 錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true; // 阻止應用程式崩潰
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Domain Exception] {ex}");
                }
            };
        }
    }
}
