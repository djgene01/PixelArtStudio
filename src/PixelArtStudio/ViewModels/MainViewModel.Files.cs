using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Services;

namespace PixelArtStudio.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    public bool ConfirmDiscardChanges()
    {
        FinishPendingEdits();
        if (!IsDirty) return true;
        return _dialogs.Ask($"Save changes to {_documentName}?", "PixelArtStudio", MessageBoxButton.YesNoCancel) switch
        {
            MessageBoxResult.Yes => SaveDocument(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    [RelayCommand]
    private void New()
    {
        if (!ConfirmDiscardChanges()) return;
        if (_dialogs.AskNewDocument(Document.Width, Document.Height) is not { } size) return;
        LoadDocument(SpriteDocument.Create(size.Width, size.Height), null, "Untitled");
        StatusMessage = $"New {size.Width} x {size.Height} sprite";
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscardChanges()) return;
        if (_dialogs.OpenFile(DialogService.OpenFilter, "Open") is { } path) OpenPath(path);
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (path != null && ConfirmDiscardChanges()) OpenPath(path);
    }

    /// <summary>Opens a .pxs project, or an image file as a new unsaved sprite.</summary>
    public void OpenPath(string path)
    {
        try
        {
            if (string.Equals(Path.GetExtension(path), ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                LoadDocument(ProjectFile.Load(path), path, StripExtension(path));
                AddRecent(path);
                StatusMessage = $"Opened {Path.GetFileName(path)}";
            }
            else
            {
                var doc = ImageImporter.LoadDocument(path);
                LoadDocument(doc, null, StripExtension(path));
                StatusMessage = $"Imported {Path.GetFileName(path)} ({doc.Frames.Count} frame(s)). Save to keep it as a .pxs sprite.";
            }
        }
        catch (Exception ex)
        {
            if (!File.Exists(path)) RemoveRecent(path);
            _dialogs.ShowError($"Could not open {Path.GetFileName(path)}.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Save() => SaveDocument();

    [RelayCommand]
    private void SaveAs() => SaveDocumentAs();

    public bool SaveDocument() => FilePath == null ? SaveDocumentAs() : SaveTo(FilePath);

    private bool SaveDocumentAs()
    {
        string? path = _dialogs.SaveFile(DialogService.ProjectFilter, "Save As", _documentName + ProjectFile.Extension);
        return path != null && SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        FinishPendingEdits();
        try
        {
            ProjectFile.Save(Document, path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Could not save {Path.GetFileName(path)}.\n\n{ex.Message}");
            return false;
        }
        AfterSave(path);
        return true;
    }

    /// <summary>Bookkeeping after <paramref name="path"/> was written: name, path, clean marker and recent files.</summary>
    private void AfterSave(string path)
    {
        _documentName = StripExtension(path);
        FilePath = path;
        History.MarkSaved();
        AddRecent(path);
        StatusMessage = $"Saved {Path.GetFileName(path)}";
    }

    [RelayCommand]
    private void ImportSheet()
    {
        if (!ConfirmDiscardChanges()) return;
        string? path = _dialogs.OpenFile(DialogService.ImageFilter, "Import Sprite Sheet");
        if (path == null) return;
        try
        {
            var sheet = ImageImporter.LoadPixels(path);
            if (_dialogs.AskSheetSlice(sheet.Width, sheet.Height) is not { } slice) return;
            var doc = SpriteSheet.Slice(sheet, slice.FrameWidth, slice.FrameHeight, slice.SkipEmpty, slice.DurationMs);
            LoadDocument(doc, null, StripExtension(path));
            StatusMessage = $"Imported {doc.Frames.Count} frame(s) from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Could not import {Path.GetFileName(path)}.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        FinishPendingEdits();
        StopPlayback();
        if (_dialogs.AskExport(Document.Frames.Count, _activeFrame) is not { } options) return;

        string ext = ImageExporter.Extension(options.Format);
        string label = options.Format switch
        {
            ExportFormat.Gif => "GIF image",
            ExportFormat.WebP => "WebP image",
            ExportFormat.Bmp => "Bitmap image",
            _ => "PNG image",
        };
        string? path = _dialogs.SaveFile($"{label} (*{ext})|*{ext}", "Export", _documentName + ext);
        if (path == null) return;

        var snapshot = Document.Clone();
        IsBusy = true;
        StatusMessage = "Exporting...";
        try
        {
            var files = await Task.Run(() => ImageExporter.Export(snapshot, path, options));
            StatusMessage = files.Count == 1
                ? $"Exported {Path.GetFileName(files[0])}"
                : $"Exported {files.Count} files to {Path.GetDirectoryName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed";
            _dialogs.ShowError($"Export failed.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddRecent(string path)
    {
        RemoveRecent(path, save: false);
        RecentFilePaths.Insert(0, path);
        while (RecentFilePaths.Count > RecentFiles.MaxCount) RecentFilePaths.RemoveAt(RecentFilePaths.Count - 1);
        RecentFiles.Save(RecentFilePaths);
    }

    private void RemoveRecent(string path, bool save = true)
    {
        var existing = RecentFilePaths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return;
        RecentFilePaths.Remove(existing);
        if (save) RecentFiles.Save(RecentFilePaths);
    }
}
