using CommunityToolkit.Mvvm.ComponentModel;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.ViewModels;

/// <summary>A row in the colour cycle list. Edits go through the main view model so they are undoable.</summary>
public sealed class CycleViewModel(MainViewModel owner) : ObservableObject
{
    private string _name = "";
    private int _stepMs;
    private bool _reverse;
    private bool _enabled;

    public int Index { get; private set; }
    public int First { get; private set; }
    public int Last { get; private set; }
    public string RangeText => $"Slots {First}-{Last}";

    public string Name
    {
        get => _name;
        set
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0 || trimmed == _name) { OnPropertyChanged(); return; }
            _name = trimmed;
            owner.EditCycle(Index, "Rename colour cycle", c => c.Name = trimmed);
        }
    }

    /// <summary>Milliseconds per step. Out-of-range values are clamped by the model.</summary>
    public int StepMs
    {
        get => _stepMs;
        set
        {
            if (value == _stepMs) return;
            owner.EditCycle(Index, "Colour cycle speed", c => c.StepMs = value, mergeKey: $"cycle-step:{Index}");
        }
    }

    public bool Reverse
    {
        get => _reverse;
        set
        {
            if (!SetProperty(ref _reverse, value)) return;
            owner.EditCycle(Index, "Colour cycle direction", c => c.Reverse = value);
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            owner.EditCycle(Index, value ? "Enable colour cycle" : "Disable colour cycle", c => c.Enabled = value);
        }
    }

    internal void Sync(int index, ColorCycle cycle)
    {
        Index = index;
        First = cycle.First;
        Last = cycle.Last;
        _name = cycle.Name;
        _stepMs = cycle.StepMs;
        _reverse = cycle.Reverse;
        _enabled = cycle.Enabled;
        OnPropertyChanged(string.Empty);
    }
}
