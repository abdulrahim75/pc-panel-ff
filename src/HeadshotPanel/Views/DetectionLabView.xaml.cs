using System.Windows.Controls;
using System.Windows;
using HeadshotPanel.ViewModels;
using Microsoft.Win32;

namespace HeadshotPanel.Views;

public partial class DetectionLabView : UserControl
{
    public DetectionLabView()
    {
        InitializeComponent();
        Loaded += ConfigureFileDialogs;
    }

    private void ConfigureFileDialogs(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.ChooseVideo = _ =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select prerecorded video",
                Filter = "Video files (*.mp4;*.avi;*.mkv;*.mov)|*.mp4;*.avi;*.mkv;*.mov|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                viewModel.SelectVideo(dialog.FileName);
        };

        viewModel.ChooseModel = _ =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select local YOLO model",
                Filter = "YOLO model (*.pt)|*.pt|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                viewModel.SelectModel(dialog.FileName);
        };
    }
}
