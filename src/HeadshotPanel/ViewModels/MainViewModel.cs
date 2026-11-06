using HeadshotPanel.Models;
using HeadshotPanel.Services;
using HeadshotPanel.Views;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace HeadshotPanel.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private NavigationPage _currentPage = NavigationPage.Dashboard;
    private FrameworkElement _currentView;
    private string _videoPath = string.Empty;
    private string _modelPath = string.Empty;
    private string _engineStatus = "Stopped";
    private string _processError = string.Empty;
    private string _engineOutput = string.Empty;
    private BitmapSource? _previewFrame;

    public MainViewModel()
    {
        StatusService = new ApplicationStatusService();
        PythonEngineService = new PythonEngineService();
        PythonEngineService.StatusChanged += (_, _) =>
        {
            RunOnUi(() =>
            {
                EngineStatus = PythonEngineService.IsRunning ? "Running" : "Stopped";
                CommandManager.InvalidateRequerySuggested();
            });
        };
        PythonEngineService.ErrorReceived += (_, message) =>
        {
            RunOnUi(() => ProcessError = message);
        };
        PythonEngineService.OutputReceived += (_, message) =>
        {
            RunOnUi(() => EngineOutput = message);
        };
        PythonEngineService.FrameReceived += (_, jpeg) =>
        {
            RunOnUi(() => UpdatePreviewFrame(jpeg));
        };
        PythonEngineService.FrameErrorReceived += (_, message) =>
        {
            RunOnUi(() => ProcessError = $"Frame processing error: {message}");
        };
        PythonEngineService.ProcessExited += (_, exitCode) =>
        {
            if (exitCode != 0 && string.IsNullOrWhiteSpace(ProcessError))
                RunOnUi(() => ProcessError = $"Python engine exited with code {exitCode}.");
        };
        _currentView = new DashboardView { DataContext = this };
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavigationPage page)
                CurrentPage = page;
        });
        SelectVideoCommand = new RelayCommand(SelectVideo, () => !PythonEngineService.IsRunning);
        SelectModelCommand = new RelayCommand(SelectModel, () => !PythonEngineService.IsRunning);
        StartEngineCommand = new RelayCommand(StartEngine,
            () => !PythonEngineService.IsRunning &&
                  !string.IsNullOrWhiteSpace(VideoPath) &&
                  !string.IsNullOrWhiteSpace(ModelPath));
        StopEngineCommand = new RelayCommand(StopEngine, () => PythonEngineService.IsRunning);
    }

    public ApplicationStatusService StatusService { get; }
    public PythonEngineService PythonEngineService { get; }

    public string VideoPath
    {
        get => _videoPath;
        set => SetProperty(ref _videoPath, value);
    }

    public string ModelPath
    {
        get => _modelPath;
        set => SetProperty(ref _modelPath, value);
    }

    public string EngineStatus
    {
        get => _engineStatus;
        set => SetProperty(ref _engineStatus, value);
    }

    public string ProcessError
    {
        get => _processError;
        set => SetProperty(ref _processError, value);
    }

    public string EngineOutput
    {
        get => _engineOutput;
        set => SetProperty(ref _engineOutput, value);
    }

    public BitmapSource? PreviewFrame
    {
        get => _previewFrame;
        private set => SetProperty(ref _previewFrame, value);
    }

    public NavigationPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetNavigationPage(value))
                return;

            CurrentView = value switch
            {
                NavigationPage.DetectionLab => new DetectionLabView(),
                NavigationPage.Settings => new SettingsView(),
                _ => new DashboardView()
            };
            CurrentView.DataContext = this;
        }
    }

    public FrameworkElement CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand SelectVideoCommand { get; }
    public RelayCommand SelectModelCommand { get; }
    public RelayCommand StartEngineCommand { get; }
    public RelayCommand StopEngineCommand { get; }

    public Action<string>? ChooseVideo { get; set; }
    public Action<string>? ChooseModel { get; set; }

    public void SelectVideo(string path) => VideoPath = path;

    public void SelectModel(string path) => ModelPath = path;

    private void SelectVideo()
    {
        ProcessError = string.Empty;
        ChooseVideo?.Invoke(VideoPath);
    }

    private void SelectModel()
    {
        ProcessError = string.Empty;
        ChooseModel?.Invoke(ModelPath);
    }

    private void StartEngine()
    {
        ProcessError = string.Empty;
        EngineOutput = string.Empty;
        PreviewFrame = null;
        if (!PythonEngineService.Start(VideoPath, ModelPath))
            ProcessError = PythonEngineService.LastError ?? "The Python engine could not be started.";
    }

    private void StopEngine()
    {
        PythonEngineService.Stop();
        PreviewFrame = null;
    }

    public void Shutdown() => PythonEngineService.Dispose();

    private void UpdatePreviewFrame(byte[] jpeg)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(jpeg, false);
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewFrame = bitmap;
        }
        catch (Exception exception)
        {
            ProcessError = $"Frame processing error: {exception.Message}";
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    private bool SetNavigationPage(NavigationPage value)
    {
        if (_currentPage == value)
            return false;

        SetProperty(ref _currentPage, value);
        return true;
    }
}
