using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using PixelArtStudio.Services;
using PixelArtStudio.Tools;
using PixelArtStudio.ViewModels;

namespace PixelArtStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new DialogService());
        DataContext = _vm;
        PixelView.Editor = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    public MainViewModel ViewModel => _vm;

    public void OpenFile(string path) => _vm.OpenPath(path);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ActiveFrameIndex) or "" && FramesList.SelectedItem is { } item)
            FramesList.ScrollIntoView(item);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_vm.ConfirmDiscardChanges()) e.Cancel = true;
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem { DataContext: string path } item && !ReferenceEquals(item, sender))
            _vm.OpenRecentCommand.Execute(path);
    }

    /// <summary>Clicking into a layer's name box selects that layer.</summary>
    private void OnLayerItemFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ListBoxItem item) item.IsSelected = true;
    }

    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        PixelView.ZoomAround(e.GetPosition(PixelView), e.Delta > 0);
        e.Handled = true;
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.Space) PixelView.SpaceHeld = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        bool typing = Keyboard.FocusedElement is TextBoxBase;

        // Enter in a text box applies the value without having to leave the box.
        if (typing && key == Key.Enter && Keyboard.FocusedElement is TextBox box)
        {
            BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
            return;
        }

        if (key == Key.Space && !typing)
        {
            PixelView.SpaceHeld = true;
            e.Handled = true;
            return;
        }

        ICommand? command = null;
        if (modifiers == ModifierKeys.Control)
        {
            command = key switch
            {
                Key.N => _vm.NewCommand,
                Key.O => _vm.OpenCommand,
                Key.S => _vm.SaveCommand,
                Key.E => _vm.ExportCommand,
                Key.Z when !typing => _vm.UndoCommand,
                Key.Y when !typing => _vm.RedoCommand,
                Key.C when !typing => _vm.CopyCommand,
                Key.X when !typing => _vm.CutCommand,
                Key.V when !typing => _vm.PasteCommand,
                Key.A when !typing => _vm.SelectAllCommand,
                Key.D when !typing => _vm.DeselectCommand,
                Key.OemPlus or Key.Add => _vm.ZoomInCommand,
                Key.OemMinus or Key.Subtract => _vm.ZoomOutCommand,
                Key.D0 or Key.NumPad0 => _vm.ZoomFitCommand,
                _ => null,
            };
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            command = key switch
            {
                Key.S => _vm.SaveAsCommand,
                Key.Z when !typing => _vm.RedoCommand,
                _ => null,
            };
        }
        else if (modifiers == ModifierKeys.None && !typing)
        {
            ToolKind? tool = key switch
            {
                Key.B => ToolKind.Pencil,
                Key.E => ToolKind.Eraser,
                Key.G => ToolKind.Fill,
                Key.I => ToolKind.Eyedropper,
                Key.L => ToolKind.Line,
                Key.U => ToolKind.Rectangle,
                Key.O => ToolKind.Ellipse,
                Key.M => ToolKind.Select,
                _ => null,
            };
            if (tool is { } t)
            {
                _vm.CurrentTool = t;
                e.Handled = true;
                return;
            }

            command = key switch
            {
                Key.X => _vm.SwapColorsCommand,
                Key.OemOpenBrackets => _vm.BrushSmallerCommand,
                Key.OemCloseBrackets => _vm.BrushBiggerCommand,
                Key.OemComma => _vm.PreviousFrameCommand,
                Key.OemPeriod => _vm.NextFrameCommand,
                Key.Delete or Key.Back => _vm.DeleteSelectionCommand,
                Key.Escape => _vm.DeselectCommand,
                Key.Enter => _vm.CommitSelectionCommand,
                _ => null,
            };
        }

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
