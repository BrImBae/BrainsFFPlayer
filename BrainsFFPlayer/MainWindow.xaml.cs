using BrainsFFPlayer.FFmpeg.GenericOptions;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BrainsFFPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isExpanded;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAVFormatOptionCollection();

            DataContext = new MainWindowViewModel();
        }

        private void InitializeAVFormatOptionCollection()
        {
            int i = 0;

            AppData.UIDispatcher.Invoke(() =>
            {
                foreach (var mode in Enum.GetValues(typeof(AVFormatOption)))
                {
                    var option = (AVFormatOption)i;
                    InputFormatComboBox.Items.Add(new ComboBoxItem() { Content = option });

                    i++;
                }

                InputFormatComboBox.SelectedIndex = 0;
            });
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            DebugMessageRichTextBox.Margin = isExpanded ? new Thickness(5, 5, 80, 5) : new Thickness(5, -300, 80, 5);

            isExpanded = !isExpanded;
        }
    }
}