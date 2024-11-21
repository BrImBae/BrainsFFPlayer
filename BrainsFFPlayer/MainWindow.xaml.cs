using BrainsFFPlayer.FFmpeg.GenericOptions;
using BrainsFFPlayer.Utility;
using MaterialDesignThemes.Wpf;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BrainsFFPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isExpanded;
        private readonly MainWindowViewModel viewModel = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void InitializeAVInputOptionCollection()
        {
            int i = 0;

            foreach (var mode in Enum.GetValues(typeof(AVInputOption)))
            {
                var option = (AVInputOption)i;
                InputFormatComboBox.Items.Add(new ComboBoxItem() { Content = option });

                i++;
            }
        }

        private void InitializeAVFormatOptionCollection()
        {
            foreach (var item in viewModel.FormatOptionItems)
            {
                // Row 컨테이너
                StackPanel rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(5)
                };

                // CheckBox
                CheckBox checkBox = new CheckBox();
                checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsChecked") { Source = item });
                CheckBoxAssist.SetCheckBoxSize(checkBox, 30);       // materialDesign:CheckBoxAssist.CheckBoxSize 설정

                // TextBlock
                TextBlock textBlock = new TextBlock
                {
                    Margin = new Thickness(10, 0, 10, 0),
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBlock.SetBinding(TextBlock.TextProperty, new Binding("Key") { Source = item });

                // TextBox
                TextBox textBox = new TextBox
                {
                    Width = 80,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBox.SetBinding(TextBox.TextProperty, new Binding("Value") { Source = item });
                textBox.SetBinding(TextBox.ToolTipProperty, new Binding("Desciption") { Source = item });

                // RowPanel에 추가
                rowPanel.Children.Add(checkBox);
                rowPanel.Children.Add(textBlock);
                rowPanel.Children.Add(textBox);

                // StackPanel에 추가
                FormatOptionStackPanel.Children.Add(rowPanel);
            }
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            DebugMessageRichTextBox.Margin = isExpanded ? new Thickness(5, 5, 80, 5) : new Thickness(5, -300, 80, 5);

            isExpanded = !isExpanded;
        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigureJsonParser.LoadContainerFromJson();

            if (AppData.Instance.VideoUrl != "") 
            {
                viewModel.VideoUrl = AppData.Instance.VideoUrl;
            }

            if(AppData.Instance.InputFormatIndex >= 0)
            {
                viewModel.SelectedInputFormatIndex = AppData.Instance.InputFormatIndex;
            }

            if (AppData.Instance.FormatOptionItems.Count > 0)
            {
                viewModel.FormatOptionItems = AppData.Instance.FormatOptionItems;
            }

            viewModel.IsHwDecoderDXVA2 = AppData.Instance.IsHwDecoderDXVA2;

            InitializeAVInputOptionCollection();
            InitializeAVFormatOptionCollection();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            AppData.Instance.VideoUrl = viewModel.VideoUrl;
            AppData.Instance.InputFormatIndex = viewModel.SelectedInputFormatIndex;
            AppData.Instance.IsHwDecoderDXVA2 = viewModel.IsHwDecoderDXVA2;
            AppData.Instance.FormatOptionItems = viewModel.FormatOptionItems;

            ConfigureJsonParser.SaveContainerToJson();
        }

    }
}