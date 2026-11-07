using System.IO;
using System.ComponentModel;
using System.Windows.Media;

namespace SpriteLens.Core;

/// <summary>胶片条里的一张图片。</summary>
public sealed class ImageFileItem : INotifyPropertyChanged
{
    private string _path;
    private ImageSource? _thumbnail;
    private int _number;

    public ImageFileItem(string path)
    {
        _path = path;
        var info = new FileInfo(path);
        Size = info.Exists ? info.Length : 0;
        Modified = info.Exists ? info.LastWriteTime : DateTime.MinValue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path => _path;

    public string Name => System.IO.Path.GetFileName(_path);

    public string Extension => System.IO.Path.GetExtension(_path).TrimStart('.').ToUpperInvariant();

    public long Size { get; private set; }

    public DateTime Modified { get; private set; }

    public int Number
    {
        get => _number;
        set
        {
            if (_number == value) return;
            _number = value;
            OnPropertyChanged(nameof(Number));
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    public void SetPath(string path)
    {
        _path = path;
        var info = new FileInfo(path);
        Size = info.Exists ? info.Length : 0;
        Modified = info.Exists ? info.LastWriteTime : DateTime.MinValue;
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Extension));
    }

    public void RefreshFileInfo()
    {
        var info = new FileInfo(_path);
        Size = info.Exists ? info.Length : 0;
        Modified = info.Exists ? info.LastWriteTime : DateTime.MinValue;
        OnPropertyChanged(nameof(Size));
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

