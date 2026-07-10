using System.Windows.Media;

namespace VideoLibrary.Models;

public class VideoItem
{
    public string FileName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public ImageSource? Thumbnail { get; set; }
}
