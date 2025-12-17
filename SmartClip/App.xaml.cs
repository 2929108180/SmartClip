using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SmartClip
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 应用系统主题
            ApplySystemTheme();

            // 监听系统主题变化
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnExit(e);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                Dispatcher.Invoke(ApplySystemTheme);
            }
        }

        private void ApplySystemTheme()
        {
            var isDarkTheme = IsSystemDarkTheme();
            ApplicationThemeManager.Apply(
                isDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.Mica,
                true
            );
        }

        private bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value != null)
                    {
                        return (int)value == 0;
                    }
                }
            }
            catch
            {
            }

            return true; // 默认使用深色主题
        }
    }
}
