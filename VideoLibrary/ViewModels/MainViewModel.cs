using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using Microsoft.WindowsAPICodePack.Shell;
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
    private static readonly string[] VideoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };

    public ObservableCollection<VideoItem> Items { get; } = new();
    public ICollectionView ItemsView { get; }
    
    private string _rootFolder = string.Empty;
    public string RootFolder
    {
        get => _rootFolder;
        set
        {
            if (_rootFolder == value) return;
            _rootFolder = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RootFolder)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilterText)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Search)));
            ItemsView.Refresh();
        }
    }

    public ICommand PickFolderCommand { get; }
    public ICommand StartScanCommand { get; }
    public ICommand ApplyFilterCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand SaveResultsCommand { get; }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSaveResults)));
            CommandManager.InvalidateRequerySuggested();
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsScanning)));
            UpdateCanSaveResults();
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set
        {
            if (Math.Abs(_progress - value) < 0.0001) return;
            _progress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = Filter;

        PickFolderCommand = new RelayCommand(async _ => await PickFolderAsync());
        StartScanCommand = new RelayCommand(async _ => await StartScanAsync());
        ApplyFilterCommand = new RelayCommand(_ => ApplyFilter());
        OpenItemCommand = new RelayCommand(p => OpenItem(p as VideoItem));
        SaveResultsCommand = new RelayCommand(_ => SaveResults(), _ => CanSaveResults);

        Items.CollectionChanged += (_, _) => UpdateCanSaveResults();

        try
        {
            var savedFolder = LoadSetting(LastFolderSettingKey);
            if (!string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder))
            {
                RootFolder = savedFolder!; // do not auto-scan on startup
            }
        }
        catch { }

        UpdateCanSaveResults();
    }

    private void ApplyFilter()
    {
        Search = FilterText ?? string.Empty;
    }

    private async Task StartScanAsync()
    {
        if (string.IsNullOrWhiteSpace(RootFolder) || !Directory.Exists(RootFolder)) return;
        await LoadFolderAsync(RootFolder);
        SaveLastFolder(RootFolder);
    }

    private void UpdateCanSaveResults()
    {
        CanSaveResults = !IsScanning && Items.Count > 0;
    }

    private bool Filter(object obj)
    {
        if (obj is not VideoItem it) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var q = Search.Trim();
        return it.FileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || it.FolderPath.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task PickFolderAsync()
    {
        // Use WinForms folder browser - must be shown on the UI thread
        string? folder = null;
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog();
                var res = dlg.ShowDialog();
                if (res == System.Windows.Forms.DialogResult.OK)
                    folder = dlg.SelectedPath;
            });
        }
        catch { }

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            RootFolder = folder!;
        }
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
                return setting.Value;

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
        catch { }
    }

    private async Task LoadFolderAsync(string folder)
    {
        Items.Clear();
        IsScanning = true;
        Progress = 0;
        StatusText = "Scanning...";
        UpdateCanSaveResults();

        await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var total = files.Length;
            for (var i = 0; i < total; i++)
            {
                var f = files[i];
                try
                {
                    var fi = new FileInfo(f);
                    var thumb = GenerateThumbnailSafe(f);
                    var item = new VideoItem
                    {
                        FileName = Path.GetFileName(f),
                        FolderPath = Path.GetDirectoryName(f) ?? string.Empty,
                        FullPath = f,
                        SizeBytes = fi.Length,
                        Thumbnail = thumb
                    };
                    System.Windows.Application.Current.Dispatcher.Invoke(() => Items.Add(item));

                    // update progress
                    var percent = total == 0 ? 100 : ((i + 1) * 100.0 / total);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Progress = percent;
                        StatusText = $"Scanning {i + 1} of {total}...";
                    });
                }
                catch { }
            }
        });

        IsScanning = false;
        Progress = 0;
        StatusText = $"Completed. {Items.Count} videos found.";
        UpdateCanSaveResults();
    }

    private void SaveResults()
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
            return;

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

    private ImageSource? GenerateThumbnailSafe(string file)
    {
        try
        {
            using var shellFile = ShellFile.FromFilePath(file);
            // Try to get a thumbnail; fallback to null on failure.
            var thumb = shellFile.Thumbnail?.ExtraLargeBitmap ?? shellFile.Thumbnail?.LargeBitmap ?? shellFile.Thumbnail?.MediumBitmap;
            if (thumb != null)
            {
                var hbitmap = thumb.GetHbitmap();
                try
                {
                    var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hbitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(64, 64));
                    src.Freeze();
                    return src;
                }
                finally
                {
                    NativeMethods.DeleteObject(hbitmap);
                }
            }
        }
        catch { }

        // Fallback: use application icon
        try
        {
            var ico = System.Drawing.SystemIcons.Application;
            using var bmp = ico.ToBitmap();
            var h = bmp.GetHbitmap();
            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
                src.Freeze();
                return src;
            }
            finally { NativeMethods.DeleteObject(h); }
        }
        catch { }

        return null;
    }

    private void OpenItem(VideoItem? item)
    {
        if (item == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
