using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using VideoLibrary.Models;

namespace VideoLibrary.ViewModels;

public class VideoExportItem
{
    public string FileName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };

    private readonly List<VideoItem> _allItems = new();

    public ObservableCollection<VideoItem> Items { get; } = new();

    private string _rootFolder = string.Empty;
    public string RootFolder
    {
        get => _rootFolder;
        set
        {
            if (_rootFolder == value) return;
            _rootFolder = value;
            OnPropertyChanged();
        }
    }

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
        }
    }

    private string _search = string.Empty;
    public string Search
    {
        get => _search;
        set
        {
            if (_search == value) return;
            _search = value;
            OnPropertyChanged();
            RefreshFilteredItems();
        }
    }

    private const string LastFolderSettingKey = "LastFolder";
    private const string LastExportPathSettingKey = "LastExportPath";

    private bool _canSaveResults;
    public bool CanSaveResults
    {
        get => _canSaveResults;
        private set
        {
            if (_canSaveResults == value) return;
            _canSaveResults = value;
            OnPropertyChanged();
        }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value) return;
            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            UpdateCanSaveResults();
        }
    }

    private bool _isLoadingResults;
    public bool IsLoadingResults
    {
        get => _isLoadingResults;
        private set
        {
            if (_isLoadingResults == value) return;
            _isLoadingResults = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            UpdateCanSaveResults();
        }
    }

    public bool IsBusy => IsScanning || IsLoadingResults;

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set
        {
            if (Math.Abs(_progress - value) < 0.0001) return;
            _progress = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel()
    {
        Items.CollectionChanged += (_, _) =>
        {
            UpdateCanSaveResults();
            OnPropertyChanged(nameof(Items));
        };

        try
        {
            var savedFolder = LoadSetting(LastFolderSettingKey);
            if (!string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder))
            {
                RootFolder = savedFolder!;
            }
        }
        catch
        {
        }

        UpdateCanSaveResults();
    }

    public void ApplyFilter()
    {
        Search = FilterText ?? string.Empty;
    }

    public async Task PickFolderAsync()
    {
        string? folder = null;
        try
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                folder = dlg.SelectedPath;
            }
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            RootFolder = folder!;
        }

        await Task.CompletedTask;
    }

    public Task StartScanAsync()
    {
        if (string.IsNullOrWhiteSpace(RootFolder) || !Directory.Exists(RootFolder) || IsBusy)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        var worker = new BackgroundWorker { WorkerReportsProgress = true };

        worker.DoWork += (_, args) =>
        {
            var folder = (string)args.Argument!;
            var discoveredItems = new List<VideoItem>();
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var total = files.Length;
            for (var i = 0; i < total; i++)
            {
                var filePath = files[i];
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    discoveredItems.Add(new VideoItem
                    {
                        FileName = Path.GetFileName(filePath),
                        FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        FullPath = filePath,
                        SizeBytes = fileInfo.Length
                    });
                }
                catch
                {
                }

                var percent = total == 0 ? 100 : (i + 1) * 100 / total;
                worker.ReportProgress(percent, $"Scanning {i + 1} of {total}...");
            }

            args.Result = discoveredItems;
        };

        worker.ProgressChanged += (_, e) =>
        {
            Progress = Convert.ToDouble(e.ProgressPercentage);
            StatusText = e.UserState as string ?? "Scanning...";
        };

        worker.RunWorkerCompleted += (_, e) =>
        {
            try
            {
                if (e.Error != null)
                {
                    StatusText = $"Could not scan folder: {e.Error.Message}";
                }
                else if (e.Result is List<VideoItem> discoveredItems)
                {
                    _allItems.Clear();
                    _allItems.AddRange(discoveredItems);
                    RefreshFilteredItems();
                    StatusText = $"Completed. {Items.Count} videos found.";
                    SaveLastFolder(RootFolder);
                }
            }
            finally
            {
                IsScanning = false;
                Progress = 0;
                UpdateCanSaveResults();
                tcs.TrySetResult(true);
            }
        };

        IsScanning = true;
        Progress = 0;
        StatusText = "Scanning...";
        UpdateCanSaveResults();
        worker.RunWorkerAsync(RootFolder);
        return tcs.Task;
    }

    public Task LoadResultsAsync()
    {
        var lastExportPath = LoadSetting(LastExportPathSettingKey);
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
            DefaultExt = "xml",
            CheckFileExists = true,
            Title = "Load Previous Results"
        };

        if (!string.IsNullOrWhiteSpace(lastExportPath))
        {
            var directory = Path.GetDirectoryName(lastExportPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
                dialog.FileName = Path.GetFileName(lastExportPath);
            }
        }

        var result = dialog.ShowDialog();
        if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return Task.CompletedTask;
        }

        return LoadResultsFromFileAsync(dialog.FileName!);
    }

    public Task LoadLastResultsAsync()
    {
        var lastExportPath = LoadSetting(LastExportPathSettingKey);
        if (string.IsNullOrWhiteSpace(lastExportPath) || !File.Exists(lastExportPath))
        {
            StatusText = "No previous results file is available.";
            return Task.CompletedTask;
        }

        return LoadResultsFromFileAsync(lastExportPath!);
    }

    public Task LoadResultsFromFileAsync(string loadPath)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        var worker = new BackgroundWorker { WorkerReportsProgress = false };

        worker.DoWork += (_, args) =>
        {
            var serializer = new XmlSerializer(typeof(List<VideoExportItem>));
            using var reader = new StreamReader((string)args.Argument!);
            var loadedItems = serializer.Deserialize(reader) as List<VideoExportItem>;
            args.Result = loadedItems;
        };

        worker.RunWorkerCompleted += (_, e) =>
        {
            try
            {
                if (e.Error != null)
                {
                    StatusText = $"Could not load results: {e.Error.Message}";
                }
                else if (e.Result is List<VideoExportItem> loadedItems)
                {
                    var videoItems = loadedItems.Select(item => new VideoItem
                    {
                        FileName = item.FileName,
                        FolderPath = item.FolderPath,
                        FullPath = item.FullPath,
                        SizeBytes = item.SizeBytes
                    }).ToList();

                    _allItems.Clear();
                    _allItems.AddRange(videoItems);
                    RefreshFilteredItems();
                    SaveSetting(LastExportPathSettingKey, loadPath);
                    StatusText = $"Loaded {videoItems.Count} previous results from {Path.GetFileName(loadPath)}.";
                }
            }
            finally
            {
                IsLoadingResults = false;
                tcs.TrySetResult(true);
            }
        };

        IsLoadingResults = true;
        StatusText = "Loading results...";
        worker.RunWorkerAsync(loadPath);
        return tcs.Task;
    }

    public void SaveResults()
    {
        if (Items.Count == 0)
        {
            StatusText = "There are no results to save yet.";
            return;
        }

        var lastExportPath = LoadSetting(LastExportPathSettingKey);
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
            DefaultExt = "xml",
            FileName = Path.GetFileName(lastExportPath) ?? "VideoLibraryResults.xml"
        };

        if (!string.IsNullOrWhiteSpace(lastExportPath))
        {
            var directory = Path.GetDirectoryName(lastExportPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        var result = dialog.ShowDialog();
        if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            var exportFileName = dialog.FileName ?? string.Empty;
            var exportItems = Items.Select(item => new VideoExportItem
            {
                FileName = item.FileName,
                FolderPath = item.FolderPath,
                FullPath = item.FullPath,
                SizeBytes = item.SizeBytes
            }).ToList();

            var serializer = new XmlSerializer(typeof(List<VideoExportItem>));
            using var writer = new StreamWriter(exportFileName);
            serializer.Serialize(writer, exportItems);

            SaveSetting(LastExportPathSettingKey, exportFileName);
            StatusText = $"Saved {exportItems.Count} videos to {Path.GetFileName(exportFileName)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not save results: {ex.Message}";
        }
    }

    public void OpenItem(VideoItem? item)
    {
        if (item == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void RefreshFilteredItems()
    {
        Items.Clear();
        var query = Search.Trim();
        var filtered = _allItems.Where(item => string.IsNullOrWhiteSpace(query)
            || item.FileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || item.FolderPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ItemsChanged));
        UpdateCanSaveResults();
    }

    private void UpdateCanSaveResults()
    {
        CanSaveResults = !IsScanning && !IsLoadingResults && Items.Count > 0;
    }

    private void SaveLastFolder(string folder)
    {
        SaveSetting(LastFolderSettingKey, folder);
    }

    private string? LoadSetting(string key)
    {
        try
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var setting = config.AppSettings.Settings[key];
            if (setting?.Value is not null)
            {
                return setting.Value;
            }

            return ConfigurationManager.AppSettings[key];
        }
        catch
        {
            return null;
        }
    }

    private void SaveSetting(string key, string? value)
    {
        try
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var setting = config.AppSettings.Settings[key];
            if (setting is null)
            {
                config.AppSettings.Settings.Add(key, value ?? string.Empty);
            }
            else
            {
                setting.Value = value ?? string.Empty;
            }

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string ItemsChanged => string.Empty;
}
