using SafeFormatter.Models;
using SafeFormatter.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SafeFormatter.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DiskService _svc = new();

        public ObservableCollection<DiskInfo> Disks { get; } = new();

        private DiskInfo? _selected;
        public DiskInfo? Selected
        {
            get => _selected;
            set { _selected = value; OnPropertyChanged(); UpdateFsPreview(); }
        }

        private string _volumeLabel = string.Empty;
        public string VolumeLabel { get => _volumeLabel; set { _volumeLabel = value; OnPropertyChanged(); } }

        private string _fsPreview = string.Empty;
        public string FsPreview { get => _fsPreview; set { _fsPreview = value; OnPropertyChanged(); } }

        private bool _confirmed;
        public bool Confirmed { get => _confirmed; set { _confirmed = value; OnPropertyChanged(); } }

        private string _serialDisplay = string.Empty;
        public string SerialDisplay { get => _serialDisplay; set { _serialDisplay = value; OnPropertyChanged(); } }

        private string _logText = string.Empty;
        public string LogText { get => _logText; set { _logText = value; OnPropertyChanged(); } }

        private double _progress;
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

        private string _elapsed = "00:00";
        public string Elapsed { get => _elapsed; set { _elapsed = value; OnPropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private readonly Stopwatch _sw = new();

        public ICommand RefreshCmd { get; }
        public ICommand StartCmd { get; }

        public MainViewModel()
        {
            RefreshCmd = new RelayCommand(_ => Refresh());
            StartCmd = new RelayCommand(async _ => await StartAsync(), _ => CanStart());
            Refresh();
        }

        private bool CanStart() => Selected != null && Confirmed && !IsBusy;

        public void Refresh()
        {
            Disks.Clear();
            foreach (var d in _svc.GetRemovableDisks())
                Disks.Add(d);
            UpdateFsPreview();
        }

        private void UpdateFsPreview()
        {
            SerialDisplay = Selected?.Serial ?? string.Empty;
            FsPreview = Selected?.RecommendedFs ?? string.Empty;
        }

        private async Task StartAsync()
        {
            if (Selected == null) return;
            IsBusy = true; Progress = 0; LogText = string.Empty; _sw.Restart(); UpdateTimer();

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, __) => UpdateTimer();
            timer.Start();

            void Log(string s) => Application.Current.Dispatcher.Invoke(() => LogText += s + "\n");
            void Prog(double p) => Application.Current.Dispatcher.Invoke(() => Progress = p);

            var (ok, userMessage, rawLog, logFile) = await _svc.CleanAllAndFormatAsync(Selected, VolumeLabel, Log, Prog);

            timer.Stop(); _sw.Stop(); UpdateTimer(); IsBusy = false; Progress = ok ? 1 : Progress;

            MessageBox.Show(userMessage + (string.IsNullOrWhiteSpace(logFile) ? string.Empty : $"\n\nLog: {logFile}"), ok ? "Erfolg" : "Fehler",
                MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void UpdateTimer()
        {
            Elapsed = _sw.IsRunning ? _sw.Elapsed.ToString("mm':'ss") : Elapsed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class RelayCommand : ICommand
    {
        private readonly Predicate<object?>? _can;
        private readonly Action<object?> _exec;
        public RelayCommand(Action<object?> exec, Predicate<object?>? can = null) { _exec = exec; _can = can; }
        public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _exec(parameter);
        public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }
}