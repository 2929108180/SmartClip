using System.Windows;
using System.Windows.Media.Animation;

namespace SmartClip.Views
{
    public partial class ConfirmDialog : Window
    {
        public bool IsConfirmed { get; private set; }

        public ConfirmDialog(string title, string message, string confirmText = "确认")
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = (Storyboard)FindResource("FadeIn");
            BeginStoryboard(fadeIn);
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }
    }
}
