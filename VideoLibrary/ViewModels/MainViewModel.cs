using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.WindowsAPICodePack.Shell;
using VideoLibrary.Models;

namespace VideoLibrary.ViewModels;

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

    private string? _lastFolderFile = null;

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value) return;
            _isScanning = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsScanning)));
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

        _lastFolderFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoLibrary", "last_folder.txt");
        try
        {
            if (File.Exists(_lastFolderFile))
            {
                var last = File.ReadAllText(_lastFolderFile).Trim();
                if (Directory.Exists(last))
                    RootFolder = last; // do not auto-scan on startup
            }
        }
        catch { }
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
            RootFolder = folder;
        }
    }

    private void SaveLastFolder(string folder)
    {
        try
        {
            var dir = Path.GetDirectoryName(_lastFolderFile!)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_lastFolderFile!, folder);
        }
        catch { }
    }

    private async Task LoadFolderAsync(string folder)
    {
        Items.Clear();
        IsScanning = true;
        Progress = 0;
        StatusText = "Scanning...";

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
