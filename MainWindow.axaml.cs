using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AsmResolver.DotNet;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CosturaDecompressor;

public partial class MainWindow : Window
{
    private TextBox? _logBox;
    private ListBox? _filesList;
    private ProgressBar? _progress;
    private TextBlock? _statusText;
    private TextBlock? _fileCountText;
    private readonly ObservableCollection<string> _files = new();
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        SetupUI();
    }

    private void SetupUI()
    {
        _logBox = this.FindControl<TextBox>("LogBox") ?? throw new InvalidOperationException("LogBox not found");
        _filesList = this.FindControl<ListBox>("FilesList") ?? throw new InvalidOperationException("FilesList not found");
        _progress = this.FindControl<ProgressBar>("Progress") ?? throw new InvalidOperationException("Progress not found");
        _statusText = this.FindControl<TextBlock>("StatusText") ?? throw new InvalidOperationException("StatusText not found");
        _fileCountText = this.FindControl<TextBlock>("FileCountText") ?? throw new InvalidOperationException("FileCountText not found");

        _filesList.DataContext = _files;

        this.FindControl<Button>("BrowseButton")!.Click += OpenFiles_Click;
        this.FindControl<Button>("RemoveButton")!.Click += RemoveFile_Click;
        this.FindControl<Button>("ExtractButton")!.Click += Extract_Click;
        this.FindControl<Button>("ClearButton")!.Click += Clear_Click;

        Log("Ready");
    }

    private async void OpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Files to Extract",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*.dll", "*.compressed" } } }
        });

        if (files.Count > 0)
        {
            foreach (var file in files)
                _files.Add(file.Path.LocalPath);
            UpdateCount();
            Log($"Added {files.Count} file(s)");
        }
    }

    private void RemoveFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_filesList?.SelectedItem is string selected)
        {
            _files.Remove(selected);
            UpdateCount();
            Log($"Removed {Path.GetFileName(selected)}");
        }
    }

    private async void Extract_Click(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0)
        {
            Log("No files selected");
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            Update("Preparing...", 0);

            if (_files.Count == 1 && _files[0].EndsWith(".compressed", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractSingleCompressed(_files[0], _cts.Token);
            }
            else
            {
                var outFolder = await SelectOutputFolder();
                if (!string.IsNullOrEmpty(outFolder))
                    await ExtractBatch(_files, outFolder, _cts.Token);
                else
                    Update("Cancelled", 0);
            }
        }
        catch (OperationCanceledException)
        {
            Update("Cancelled", 0);
            Log("Operation cancelled");
        }
        catch (Exception ex)
        {
            Update("Error", 0);
            Log($"Error: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        _files.Clear();
        _progress!.Value = 0;
        Update("Ready", 0);
        Log("Cleared all files");
    }

    private async Task ExtractSingleCompressed(string file, CancellationToken ct)
    {
        var storage = StorageProvider;
        var dialog = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Decompressed File",
            FileTypeChoices = new[] { new FilePickerFileType("Decompressed Files") { Patterns = new[] { "*.exe", "*.dll", "*" } } }
        });

        if (dialog != null)
        {
            await Task.Run(() =>
            {
                var data = File.ReadAllBytes(file);
                var decompressed = data.Decompress();
                File.WriteAllBytes(dialog.Path.LocalPath, decompressed);
            }, ct);

            Update("Completed", 100);
            Log($"Extracted {Path.GetFileName(file)}");
        }
    }

    private async Task ExtractBatch(ObservableCollection<string> files, string outputPath, CancellationToken ct)
    {
        _progress!.Maximum = files.Count;
        
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var file = files[i];
                var name = Path.GetFileName(file);
                Update($"Processing {i + 1}/{files.Count}...", i);

                if (file.EndsWith(".compressed", StringComparison.OrdinalIgnoreCase))
                {
                    await DecompressFile(file, outputPath);
                }
                else if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractAssembly(file, outputPath);
                }
                else
                {
                    Log($"⚠️  Unsupported: {name}");
                    continue;
                }

                Log($"✓ {name}");
            }
            catch (Exception ex)
            {
                Log($"✗ {Path.GetFileName(files[i])}: {ex.Message}");
            }

            _progress!.Value = i + 1;
        }

        Update("Completed", 100);
    }

    private async Task DecompressFile(string file, string outputPath)
    {
        await Task.Run(() =>
        {
            var data = File.ReadAllBytes(file);
            var decompressed = data.Decompress();
            var outFile = Path.Combine(outputPath, Path.GetFileName(file).Replace(".compressed", ""));
            File.WriteAllBytes(outFile, decompressed);
        });
    }

    private async Task ExtractAssembly(string file, string outputPath)
    {
        await Task.Run(() =>
        {
            var module = ModuleDefinition.FromFile(file);
            var extractor = new ExtractorNew(module);
            extractor.Run(outputPath);
        });
    }

    private async Task<string?> SelectOutputFolder()
    {
        var storage = StorageProvider;
        var folder = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder"
        });

        return folder.Count > 0 ? folder[0].Path.LocalPath : null;
    }

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_logBox != null)
                _logBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        });
    }

    private void Update(string status, double progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_statusText != null)
                _statusText.Text = status;
            if (_progress != null)
                _progress.Value = progress;
        });
    }

    private void UpdateCount()
    {
        if (_fileCountText != null)
            _fileCountText.Text = $"{_files.Count} file{(_files.Count != 1 ? "s" : "")}";
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
