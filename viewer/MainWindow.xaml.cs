using System.IO;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClxViewer.Services;
using Microsoft.Win32;

namespace ClxViewer;

public partial class MainWindow : Window
{
    private enum ViewMode { Fluo, Bright, Merged }

    private sealed class ImageChannel
    {
        public int Index;
        public int Channel; // 0 = brightfield, 1 = fluorescence, -1 unknown
        public long Width, Height;
        public int Bits;
        public long DataMax;
        public ushort[] Pixels = Array.Empty<ushort>();
        public ulong[] Histogram = Array.Empty<ulong>();
        public long N;
    }

    private IntPtr _handle;
    private readonly List<string> _files = new();
    private int _currentIndex = -1;
    private string _sourcePath = "";
    private string _customExportDir = "";

    private readonly List<ImageChannel> _channels = new();
    private ImageChannel? _bf, _fluo;

    private ViewMode _view = ViewMode.Fluo;
    private bool _invert = true;
    private Levels _fluoLevels = new(0, 65535, 1);
    private Levels _bfLevels = new(0, 65535, 1);
    private bool _fluoAuto = true;
    private int _sliderChannel = 1; // merged mode: 0 = bf, 1 = fluo

    private WriteableBitmap? _wb;
    private readonly BitmapPalette _grayPalette;
    private bool _syncingSidebar;
    private bool _isPanning;
    private Point _panStart;
    private double _zoom = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        var grays = new List<Color>(256);
        for (int i = 0; i < 256; i++)
        {
            byte v = (byte)i;
            grays.Add(Color.FromRgb(v, v, v));
        }
        _grayPalette = new BitmapPalette(grays);
        SidebarToggle.Checked += (_, _) => Sidebar.Visibility = Visibility.Visible;
        SidebarToggle.Unchecked += (_, _) => Sidebar.Visibility = Visibility.Collapsed;
        MetaToggle.Checked += (_, _) => MetaPanel.Visibility = Visibility.Visible;
        MetaToggle.Unchecked += (_, _) => MetaPanel.Visibility = Visibility.Collapsed;

        if (App.CleanView)
        {
            Sidebar.Visibility = Visibility.Collapsed;
            MetaPanel.Visibility = Visibility.Collapsed;
            SidebarToggle.IsChecked = false;
            MetaToggle.IsChecked = false;
        }
    }

    // ------------------------------------------------------------------ open

    public void OpenFiles(IEnumerable<string> paths)
    {
        var list = paths.Where(p => p.EndsWith(".clx", StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return;

        _files.Clear();
        if (list.Count == 1)
        {
            // Convenience: populate navigation with all .clx in the folder.
            string dir = Path.GetDirectoryName(list[0]) ?? "";
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*.clx").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    _files.Add(f);
            }
            catch { _files.Add(list[0]); }
        }
        else
        {
            _files.AddRange(list);
        }

        LoadIndex(_files.IndexOf(list[0]));
    }

    private void LoadIndex(int index)
    {
        if (index < 0 || index >= _files.Count) return;
        _currentIndex = index;
        LoadFile(_files[index]);
    }

    private void LoadFile(string path)
    {
        if (_handle != IntPtr.Zero)
        {
            ClxReaderNative.clxr_close(_handle);
            _handle = IntPtr.Zero;
        }

        _channels.Clear();
        _bf = null;
        _fluo = null;

        _handle = ClxReaderNative.clxr_open(path);
        if (_handle == IntPtr.Zero)
        {
            MessageBox.Show(this, ClxReaderNative.LastError(), "Cannot open file",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _sourcePath = path;
        int count = ClxReaderNative.clxr_image_count(_handle);
        for (int i = 0; i < count; i++)
        {
            var ch = new ImageChannel();
            ClxReaderNative.clxr_image_info_get(_handle, i, out var info);
            ch.Index = i;
            ch.Channel = info.Channel;
            ch.Width = info.Width;
            ch.Height = info.Height;
            ch.Bits = (int)info.BitsPerSample;
            ch.DataMax = ch.Bits == 16 ? 65535 : 255;

            long bytes = info.ByteCount;
            if (bytes > 0)
            {
                var buf = new byte[bytes];
                ClxReaderNative.clxr_copy_image_pixels(_handle, i, buf, bytes);
                ch.Pixels = new ushort[bytes / 2];
                Buffer.BlockCopy(buf, 0, ch.Pixels, 0, (int)bytes);
                ch.Histogram = AutoLevels.Histogram(ch.Pixels);
                ch.N = ch.Pixels.Length;
            }
            _channels.Add(ch);
        }

        if (_channels.Count == 0)
        {
            MessageBox.Show(this, "No images found in this .clx file.", "Clx Viewer");
            return;
        }

        _bf = _channels.FirstOrDefault(c => c.Channel == 0) ?? _channels[0];
        _fluo = _channels.FirstOrDefault(c => c.Channel == 1)
                ?? (_channels.Count > 1 ? _channels[1] : _channels[0]);

        _fluoLevels = _fluo.N > 0 ? AutoLevels.Fluo(_fluo.Histogram, _fluo.N, _fluo.DataMax) : new Levels(0, _fluo.DataMax, FluoGainDefault());
        _bfLevels = _bf.N > 0 ? AutoLevels.Brightfield(_bf.Histogram, _bf.N, _bf.DataMax) : new Levels(0, _bf.DataMax, 1);
        _fluoAuto = true;

        _view = ViewMode.Fluo;
        _invert = true;
        ViewFluo.IsChecked = true;
        InvertBtn.IsChecked = true;

        _zoom = 1.0;
        SyncSliderToTarget();
        RenderImage();
        FitToWindow();
        RefreshSidebar();
        RefreshButtons();
        BuildMetadata();
        UpdateStatus();
    }

    private static double FluoGainDefault() => AutoLevels.FluoGain;

    // ---------------------------------------------------------------- rendering

    private void RenderImage()
    {
        if (_channels.Count == 0) return;
        byte[] bytes;
        int w, h;
        var ch = _channels[0];

        if (_view == ViewMode.Fluo && _fluo != null)
        {
            ch = _fluo;
            bool rev = _invert;
            double gain = rev ? _fluoLevels.Gain : 1.0;
            bytes = Renderer.Map(ch.Pixels, _fluoLevels.Low, _fluoLevels.High, rev, gain);
        }
        else if (_view == ViewMode.Bright && _bf != null)
        {
            ch = _bf;
            bytes = Renderer.Map(ch.Pixels, _bfLevels.Low, _bfLevels.High, false, 1.0);
        }
        else if (_view == ViewMode.Merged && _bf != null && _fluo != null)
        {
            ch = _bf;
            var bf = Renderer.Map(_bf.Pixels, _bfLevels.Low, _bfLevels.High, false, 1.0);
            var flu = Renderer.Map(_fluo.Pixels, _fluoLevels.Low, _fluoLevels.High, false, 1.0);
            bytes = Renderer.Merge(bf, flu);
        }
        else
        {
            bytes = Renderer.Map(ch.Pixels, 0, ch.DataMax, false, 1.0);
        }
        w = (int)ch.Width;
        h = (int)ch.Height;

        if (_wb == null || _wb.PixelWidth != w || _wb.PixelHeight != h)
        {
            _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, _grayPalette);
            Img.Source = _wb;
            Img.Width = w;
            Img.Height = h;
        }
        _wb.WritePixels(new Int32Rect(0, 0, w, h), bytes, w, 0);

        EmptyHint.Visibility = Visibility.Collapsed;
        UpdateReadouts();
    }

    // ---------------------------------------------------------------- slider

    private ImageChannel TargetChannel()
    {
        return _view switch
        {
            ViewMode.Fluo => _fluo ?? _channels[0],
            ViewMode.Bright => _bf ?? _channels[0],
            _ => _sliderChannel == 0 ? (_bf ?? _channels[0]) : (_fluo ?? _channels[0]),
        };
    }

    private void ApplyLevels(double low, double high, double gain)
    {
        switch (_view)
        {
            case ViewMode.Fluo:
                _fluoLevels = new Levels(low, high, gain);
                _fluoAuto = false;
                break;
            case ViewMode.Bright:
                _bfLevels = new Levels(low, high, 1.0);
                break;
            default:
                if (_sliderChannel == 0)
                {
                    _bfLevels = new Levels(low, high, 1.0);
                    }
                else
                {
                    _fluoLevels = new Levels(low, high, gain);
                    _fluoAuto = false;
                }
                break;
        }
    }

    private void SyncSliderToTarget()
    {
        var ch = TargetChannel();
        RangeSlider.DataMin = 0;
        RangeSlider.DataMax = ch.DataMax;
        var lv = _view switch
        {
            ViewMode.Fluo => _fluoLevels,
            ViewMode.Bright => _bfLevels,
            _ => _sliderChannel == 0 ? _bfLevels : _fluoLevels,
        };
        RangeSlider.LowValue = MathEx.Clamp(lv.Low, 0, ch.DataMax);
        RangeSlider.HighValue = MathEx.Clamp(lv.High, 0, ch.DataMax);
    }

    private void UpdateReadouts()
    {
        var lv = _view switch
        {
            ViewMode.Fluo => _fluoLevels,
            ViewMode.Bright => _bfLevels,
            _ => _sliderChannel == 0 ? _bfLevels : _fluoLevels,
        };
        MinBox.Text = ((long)Math.Round(lv.Low)).ToString();
        MaxBox.Text = ((long)Math.Round(lv.High)).ToString();
        bool merged = _view == ViewMode.Merged;
        SliderChannelBox.Visibility = merged ? Visibility.Visible : Visibility.Collapsed;
        InvertBtn.IsEnabled = _view == ViewMode.Fluo;
    }

    private void OnRangeSliderChanged(object sender, RoutedEventArgs e)
    {
        double low = RangeSlider.LowValue;
        double high = RangeSlider.HighValue;
        double gain = _view == ViewMode.Fluo && _fluoAuto ? _fluoLevels.Gain : 1.0;
        if (_view == ViewMode.Merged && _sliderChannel == 1 && _fluoAuto) gain = _fluoLevels.Gain;
        ApplyLevels(low, high, gain);
        RenderImage();
    }

    private void OnAuto(object sender, RoutedEventArgs e)
    {
        if (_channels.Count == 0) return;
        var ch = TargetChannel();
        Levels lv;
        bool isFluo = _view == ViewMode.Fluo || (_view == ViewMode.Merged && _sliderChannel == 1);
        if (isFluo && ch == _fluo)
        {
            lv = AutoLevels.Fluo(ch.Histogram, ch.N, ch.DataMax);
            _fluoLevels = lv;
            _fluoAuto = true;
        }
        else
        {
            lv = AutoLevels.Brightfield(ch.Histogram, ch.N, ch.DataMax);
            _bfLevels = lv;
        }
        SyncSliderToTarget();
        RenderImage();
    }

    private void OnMinMaxBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_channels.Count == 0) return;
        if (!long.TryParse(MinBox.Text, out long lo)) lo = 0;
        if (!long.TryParse(MaxBox.Text, out long hi)) hi = 0;
        var ch = TargetChannel();
        lo = MathEx.Clamp(lo, 0, ch.DataMax);
        hi = MathEx.Clamp(hi, 0, ch.DataMax);
        if (lo > hi) (lo, hi) = (hi, lo);
        double gain = _view == ViewMode.Fluo && _fluoAuto ? _fluoLevels.Gain : 1.0;
        if (_view == ViewMode.Merged && _sliderChannel == 1 && _fluoAuto) gain = _fluoLevels.Gain;
        ApplyLevels(lo, hi, gain);
        SyncSliderToTarget();
        RenderImage();
    }

    private void OnSliderChannelChanged(object sender, RoutedEventArgs e)
    {
        _sliderChannel = SliderChannelBox.SelectedIndex == 0 ? 0 : 1;
        if (_channels.Count == 0) return;
        SyncSliderToTarget();
        UpdateReadouts();
    }

    // ---------------------------------------------------------------- view

    private void OnViewChecked(object sender, RoutedEventArgs e)
    {
        ViewMode mode = _view;
        if (ReferenceEquals(sender, ViewFluo)) mode = ViewMode.Fluo;
        else if (ReferenceEquals(sender, ViewBright)) mode = ViewMode.Bright;
        else if (ReferenceEquals(sender, ViewMerged)) mode = ViewMode.Merged;
        if (mode == _view && _channels.Count == 0) return;
        _view = mode;
        if (_channels.Count == 0) return;
        SyncSliderToTarget();
        RenderImage();
    }

    private void OnInvertToggled(object sender, RoutedEventArgs e)
    {
        _invert = InvertBtn.IsChecked == true;
        if (_view == ViewMode.Fluo) RenderImage();
    }

    // ---------------------------------------------------------------- nav

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open .clx captures",
            Filter = "Clinx CLX captures (*.clx)|*.clx|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            OpenFiles(dlg.FileNames);
        }
    }

    private void OnPrev(object sender, RoutedEventArgs e) => Navigate(-1);
    private void OnNext(object sender, RoutedEventArgs e) => Navigate(+1);

    private void Navigate(int delta)
    {
        if (_files.Count == 0) return;
        int next = _currentIndex + delta;
        if (next < 0) next = _files.Count - 1;
        if (next >= _files.Count) next = 0;
        LoadIndex(next);
    }

    private void OnFileListSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingSidebar) return;
        if (FileList.SelectedIndex >= 0 && FileList.SelectedIndex != _currentIndex)
        {
            LoadIndex(FileList.SelectedIndex);
        }
    }

    private void RefreshSidebar()
    {
        _syncingSidebar = true;
        FileList.ItemsSource = null;
        FileList.ItemsSource = _files.Select(Path.GetFileName).ToList();
        FileList.SelectedIndex = _currentIndex;
        _syncingSidebar = false;
        FolderLabel.Text = _sourcePath.Length > 0 ? Path.GetDirectoryName(_sourcePath) ?? "—" : "—";
        FolderLabel.ToolTip = _sourcePath.Length > 0 ? Path.GetDirectoryName(_sourcePath) : null;
    }

    private void RefreshButtons()
    {
        bool has = _channels.Count > 0;
        PrevBtn.IsEnabled = has && _files.Count > 0;
        NextBtn.IsEnabled = has && _files.Count > 0;
        ExportRawBtn.IsEnabled = has;
        ExportPngBtn.IsEnabled = has;
    }

    // ---------------------------------------------------------------- export

    private string ExportDir() => _customExportDir.Length > 0
        ? _customExportDir
        : (Path.GetDirectoryName(_sourcePath) ?? "");

    private void OnChooseExportDir(object sender, RoutedEventArgs e)
    {
        var folder = ModernFolderPicker.PickFolder(
            new WindowInteropHelper(this).Handle,
            "Export folder (blank = next to the .clx file)");
        if (folder != null)
        {
            _customExportDir = folder;
            ShowToast($"Export folder → {_customExportDir}");
        }
        UpdateStatus();
    }

    private void OnExportRaw(object sender, RoutedEventArgs e) => ExportRawToDir();

    private List<string>? ExportRawToDir()
    {
        if (_handle == IntPtr.Zero) return null;
        string dir = ExportDir();
        if (dir.Length == 0) return null;
        string stem = ClxReaderNative.Stem(_handle);
        if (stem.Length == 0) stem = Path.GetFileNameWithoutExtension(_sourcePath);

        var written = new List<string>();
        for (int i = 0; i < _channels.Count; i++)
        {
            string path = Path.Combine(dir, $"{stem}_{i}_16bit.tif");
            if (ClxReaderNative.clxr_save_tiff(_handle, i, path) != 1)
            {
                MessageBox.Show(this, ClxReaderNative.LastError(), "Export failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            written.Add(path);
        }
        StatusExport.Text = $"Raw TIFF → {dir} ({written.Count} file(s))";
        ShowToast($"Exported {written.Count} raw TIFF file(s) → {Path.GetFileName(dir)}");
        return written;
    }

    private void OnExportPng(object sender, RoutedEventArgs e) => ExportPngToDir();

    private string? ExportPngToDir()
    {
        if (_channels.Count == 0) return null;
        string dir = ExportDir();
        if (dir.Length == 0) return null;
        string stem = ClxReaderNative.Stem(_handle);
        if (stem.Length == 0) stem = Path.GetFileNameWithoutExtension(_sourcePath);

        string name = _view switch
        {
            ViewMode.Fluo => $"{stem}_fluo_min{(long)Math.Round(_fluoLevels.Low)}_max{(long)Math.Round(_fluoLevels.High)}",
            ViewMode.Bright => $"{stem}_bf_min{(long)Math.Round(_bfLevels.Low)}_max{(long)Math.Round(_bfLevels.High)}",
            _ => $"{stem}_merged_bfmin{(long)Math.Round(_bfLevels.Low)}max{(long)Math.Round(_bfLevels.High)}" +
                 $"_fluomin{(long)Math.Round(_fluoLevels.Low)}max{(long)Math.Round(_fluoLevels.High)}",
        };
        string path = Path.Combine(dir, name + ".png");

        byte[] bytes = CurrentRenderedBytes();
        int w, h;
        var ch = _view == ViewMode.Fluo ? (_fluo ?? _channels[0]) : (_bf ?? _channels[0]);
        w = (int)ch.Width;
        h = (int)ch.Height;

        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, _grayPalette, bytes, w);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        try
        {
            using var fs = File.Create(path);
            enc.Save(fs);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        StatusExport.Text = $"PNG → {path}";
        ShowToast($"PNG exported → {Path.GetFileName(path)}");
        return path;
    }

    /// <summary>Headless export of raw TIFFs plus all three views — used by --selftest-export.</summary>
    public void SelftestExport(string dir)
    {
        if (_channels.Count == 0) return;
        _customExportDir = dir;
        _invert = true;
        ExportRawToDir();
        foreach (var mode in new[] { ViewMode.Fluo, ViewMode.Bright, ViewMode.Merged })
        {
            _view = mode;
            RenderImage();
            ExportPngToDir();
        }
    }

    private byte[] CurrentRenderedBytes()
    {
        // Recompute exactly what is on screen (same path as RenderImage).
        if (_view == ViewMode.Fluo && _fluo != null)
        {
            bool rev = _invert;
            double gain = rev ? _fluoLevels.Gain : 1.0;
            return Renderer.Map(_fluo.Pixels, _fluoLevels.Low, _fluoLevels.High, rev, gain);
        }
        if (_view == ViewMode.Bright && _bf != null)
        {
            return Renderer.Map(_bf.Pixels, _bfLevels.Low, _bfLevels.High, false, 1.0);
        }
        if (_view == ViewMode.Merged && _bf != null && _fluo != null)
        {
            var bf = Renderer.Map(_bf.Pixels, _bfLevels.Low, _bfLevels.High, false, 1.0);
            var flu = Renderer.Map(_fluo.Pixels, _fluoLevels.Low, _fluoLevels.High, false, 1.0);
            return Renderer.Merge(bf, flu);
        }
        var c = _channels[0];
        return Renderer.Map(c.Pixels, 0, c.DataMax, false, 1.0);
    }

    // ---------------------------------------------------------------- metadata

    private void BuildMetadata()
    {
        MetaList.Children.Clear();
        if (_channels.Count == 0)
        {
            AddMetaRow("No file loaded", "");
            return;
        }

        var json = ClxReaderNative.MetadataJson(_handle);
        try
        {
            var root = MiniJson.Parse(json);

            static string StrOf(object? v) =>
                v is string s ? s :
                v is double d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                v is bool b ? (b ? "True" : "False") : "";
            string Get(string name) => root != null && root.TryGetValue(name, out var v) ? StrOf(v) : "";

            string file = Path.GetFileName(_sourcePath);
            string sample = Get("sample_name");
            string captured = Get("capture_time");
            string exposure = Get("exposure_ms");
            string software = Get("software");
            string version = Get("format_version");
            string build = Get("build_datetime");

            AddMetaRow("File", file);
            AddMetaRow("Sample", sample);
            AddMetaRow("Captured", captured);
            AddMetaRow("Exposure", exposure.Length > 0 ? $"{exposure} ms" : "");
            AddMetaRow("Software", software.Length > 0 ? $"{software} (format v{version})" : "");
            AddMetaRow("Build date", build);
            AddMetaRow("File size", FormatBytes(new FileInfo(_sourcePath).Length));
            AddMetaRow("Folder", Path.GetDirectoryName(_sourcePath) ?? "");

            var fi = root != null && root.TryGetValue("filename_info", out var fv) ? MiniJson.Obj(fv) : null;
            if (fi != null)
            {
                AddMetaRow("Filename sample", fi.TryGetValue("sample", out var s) && s is string ss ? ss : "");
                AddMetaRow("Filename capture", fi.TryGetValue("capture_time", out var c) && c is string cc ? cc : "");
            }

            var imgs = root != null && root.TryGetValue("images", out var iv) ? MiniJson.Arr(iv) : null;
            if (imgs != null)
            {
                AddMetaRow("Images", imgs.Count.ToString());
                foreach (var item in imgs)
                {
                    var im = MiniJson.Obj(item);
                    if (im == null) continue;
                    long w = MiniJson.Num(im.TryGetValue("width", out var wp) ? wp : null);
                    long h = MiniJson.Num(im.TryGetValue("height", out var hp) ? hp : null);
                    long bits = MiniJson.Num(im.TryGetValue("bits_per_sample", out var bp) ? bp : null);
                    long mn = MiniJson.Num(im.TryGetValue("min_value", out var mnp) ? mnp : null);
                    long mx = MiniJson.Num(im.TryGetValue("max_value", out var mxp) ? mxp : null);
                    long typ = MiniJson.Num(im.TryGetValue("type", out var tp) ? tp : null);
                    long idx = MiniJson.Num(im.TryGetValue("index", out var ip) ? ip : null);
                    string label = _channels[(int)idx].Channel switch { 0 => "brightfield", 1 => "fluorescence", _ => "?" };
                    AddMetaRow($"[{idx}] {label}", $"{w}×{h}  {bits}-bit  min={mn} max={mx}  type={typ}");
                }
            }

            var tr = root != null && root.TryGetValue("trailer_info", out var tv) ? MiniJson.Obj(tv) : null;
            if (tr != null)
            {
                AddMetaRow("Trailer full scale", tr.TryGetValue("full_scale", out var fs) ? StrOf(fs) : "");
                AddMetaRow("Trailer exposure matches", tr.TryGetValue("exposure_ms_matches_header", out var em) ? StrOf(em) : "");
                if (tr.TryGetValue("Gray.pal", out var gp) && gp is string gps)
                    AddMetaRow("LUT", gps);
            }
        }
        catch (Exception)
        {
            AddMetaRow("Metadata", "Could not parse");
        }
    }

    private void AddMetaRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var val = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        MetaList.Children.Add(grid);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }

    // ---------------------------------------------------------------- status

    private void UpdateStatus()
    {
        StatusFile.Text = _sourcePath.Length > 0 ? Path.GetFileName(_sourcePath) : "No file open";
        if (_channels.Count > 0)
        {
            var c = _fluo ?? _channels[0];
            StatusDims.Text = $"{c.Width} × {c.Height}  {c.Bits}-bit";
        }
        else
        {
            StatusDims.Text = "";
        }
        StatusZoom.Text = $"{(int)Math.Round(_zoom * 100)}%";
        StatusExport.Text = _customExportDir.Length > 0 ? $"Export → {_customExportDir}" : "Export → next to file";
    }

    // ---------------------------------------------------------------- canvas

    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        if (_wb == null) return;
        Point pos = e.GetPosition(CanvasHostGrid);
        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        double old = _zoom;
        double next = MathEx.Clamp(old * factor, 0.02, 24.0);
        if (Math.Abs(next - old) < 1e-6) return;
        factor = next / old;
        PanTf.X = pos.X - factor * (pos.X - PanTf.X);
        PanTf.Y = pos.Y - factor * (pos.Y - PanTf.Y);
        ZoomTf.ScaleX = next;
        ZoomTf.ScaleY = next;
        _zoom = next;
        UpdateStatus();
        e.Handled = true;
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            FitToWindow();
            return;
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            _isPanning = true;
            _panStart = e.GetPosition(CanvasHostGrid);
            PanCanvas.Cursor = Cursors.SizeAll;
            PanCanvas.CaptureMouse();
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            Point p = e.GetPosition(CanvasHostGrid);
            PanTf.X += p.X - _panStart.X;
            PanTf.Y += p.Y - _panStart.Y;
            _panStart = p;
            return;
        }
        UpdatePixelReadout(e.GetPosition(CanvasHostGrid));
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            PanCanvas.Cursor = Cursors.Cross;
            PanCanvas.ReleaseMouseCapture();
        }
    }

    private void UpdatePixelReadout(Point p)
    {
        if (_wb == null || _channels.Count == 0)
        {
            StatusPix.Text = "";
            return;
        }
        double inv = 1.0 / _zoom;
        double ix = (p.X - PanTf.X) * inv;
        double iy = (p.Y - PanTf.Y) * inv;
        int x = (int)Math.Floor(ix);
        int y = (int)Math.Floor(iy);
        var c = TargetChannel();
        if (x < 0 || y < 0 || x >= c.Width || y >= c.Height)
        {
            StatusPix.Text = "";
            return;
        }
        int idx = y * (int)c.Width + x;
        if (idx >= c.Pixels.Length)
        {
            StatusPix.Text = "";
            return;
        }
        ushort v = c.Pixels[idx];
        StatusPix.Text = $"[{x}, {y}]  raw {v}";
    }

    private void FitToWindow()
    {
        if (_wb == null) return;
        double cw = CanvasHostGrid.ActualWidth;
        double ch = CanvasHostGrid.ActualHeight;
        if (cw <= 0 || ch <= 0) return;
        double scale = Math.Min(cw / _wb.PixelWidth, ch / _wb.PixelHeight) * 0.96;
        scale = MathEx.Clamp(scale, 0.02, 8.0);
        double sw = _wb.PixelWidth * scale;
        double sh = _wb.PixelHeight * scale;
        ZoomTf.ScaleX = scale;
        ZoomTf.ScaleY = scale;
        PanTf.X = (cw - sw) / 2.0;
        PanTf.Y = (ch - sh) / 2.0;
        _zoom = scale;
        UpdateStatus();
    }

    // ---------------------------------------------------------------- misc

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            OpenFiles(files);
        }
    }

    private DispatcherTimer? _toastTimer;

    /// <summary>Shows a short, non-blocking toast at the bottom-right of the window.</summary>
    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.RenderTransform = new TranslateTransform(0, 14);
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Toast.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
            Toast.BeginAnimation(OpacityProperty, fade);
        };
        _toastTimer.Start();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool alt = mods.HasFlag(ModifierKeys.Alt);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool inTextBox = Keyboard.FocusedElement is TextBox;

        // Ctrl+W: close
        if (ctrl && e.Key == Key.W) { Close(); e.Handled = true; return; }
        // Ctrl+O: open files
        if (ctrl && e.Key == Key.O) { OnOpen(this, new RoutedEventArgs()); e.Handled = true; return; }
        // Ctrl+Shift+E: raw TIFF
        if (ctrl && shift && e.Key == Key.E) { ExportRawToDir(); e.Handled = true; return; }
        // Ctrl+E: export PNG
        if (ctrl && !shift && e.Key == Key.E) { ExportPngToDir(); e.Handled = true; return; }
        // Alt+E: choose export folder
        if (alt && e.Key == Key.E) { OnChooseExportDir(this, new RoutedEventArgs()); e.Handled = true; return; }

        if (inTextBox) return;  // don't hijack arrows/letters while editing

        // File navigation (arrows, no Ctrl/Alt)
        if (!ctrl && !alt && e.Key == Key.Left) { Navigate(-1); e.Handled = true; return; }
        if (!ctrl && !alt && e.Key == Key.Right) { Navigate(+1); e.Handled = true; return; }

        // Plain letters (no modifiers)
        if (mods == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.F: SelectView(ViewMode.Fluo); e.Handled = true; break;
                case Key.B: SelectView(ViewMode.Bright); e.Handled = true; break;
                case Key.M: SelectView(ViewMode.Merged); e.Handled = true; break;
                case Key.A: OnAuto(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.I: InvertBtn.IsChecked = !(InvertBtn.IsChecked == true); e.Handled = true; break;
            }
        }

        // Zoom
        if (e.Key == Key.Add || e.Key == Key.OemPlus)
        {
            OnCanvasWheel(this, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, +120)); e.Handled = true;
        }
        else if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
        {
            OnCanvasWheel(this, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)); e.Handled = true;
        }
    }

    private void SelectView(ViewMode mode)
    {
        switch (mode)
        {
            case ViewMode.Fluo: ViewFluo.IsChecked = true; break;
            case ViewMode.Bright: ViewBright.IsChecked = true; break;
            case ViewMode.Merged: ViewMerged.IsChecked = true; break;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaxBtn.Content = "\uE922";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaxBtn.Content = "\uE923";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_handle != IntPtr.Zero)
        {
            ClxReaderNative.clxr_close(_handle);
            _handle = IntPtr.Zero;
        }
        base.OnClosing(e);
    }

    private void OnCanvasHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_channels.Count > 0 && e.NewSize.Width > 0)
        {
            FitToWindow();
        }
    }
}





