using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace GitSpace.Controls.Uno;

/// <summary>Compact single-selection buttons with native invocation and arrow/Home/End keyboard navigation.</summary>
public sealed class GitSegmentedSelector : Grid
{
    private readonly GitButton[] _buttons;
    private int _selectedIndex = -1;
    public event EventHandler<int>? SelectionChanged;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _buttons.Length) throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            for (var i = 0; i < _buttons.Length; i++)
            {
                _buttons[i].SetSelected(i == value);
                AutomationProperties.SetHelpText(_buttons[i], i == value ? "Selected" : "Select this view");
            }
            SelectionChanged?.Invoke(this, value);
        }
    }
    public GitSegmentedSelector(string name, params (string Text, string AccessibleName)[] choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Length == 0) throw new ArgumentException("At least one choice is required.", nameof(choices));
        AutomationProperties.SetName(this, name); ColumnSpacing = 2;
        _buttons = new GitButton[choices.Length];
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var button = new GitButton(choices[i].Text, () => SelectedIndex = index, accessibleName: choices[i].AccessibleName)
                { MinHeight = 28, Padding = new Thickness(8, 4, 8, 4) };
            _buttons[i] = button; Grid.SetColumn(button, i); Children.Add(button);
        }
        SelectedIndex = 0;
        KeyDown += (_, e) =>
        {
            var next = e.Key switch
            {
                VirtualKey.Left => (SelectedIndex + _buttons.Length - 1) % _buttons.Length,
                VirtualKey.Right => (SelectedIndex + 1) % _buttons.Length,
                VirtualKey.Home => 0,
                VirtualKey.End => _buttons.Length - 1,
                _ => -1
            };
            if (next < 0) return;
            SelectedIndex = next; _buttons[next].Focus(FocusState.Keyboard); e.Handled = true;
        };
    }
}
