using System.Windows;
using System.Windows.Controls;

namespace PixelArtStudio.Dialogs;

internal static class DialogInput
{
    /// <summary>Parses a whole number in range, or tells the user what is wrong and focuses the box.</summary>
    public static bool TryReadInt(TextBox box, int min, int max, string label, out int value)
    {
        if (int.TryParse(box.Text.Trim(), out value) && value >= min && value <= max) return true;

        MessageBox.Show(Window.GetWindow(box), $"{label} must be a whole number from {min} to {max}.", "Check the value",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
        return false;
    }
}
