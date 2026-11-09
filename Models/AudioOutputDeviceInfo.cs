using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

public partial class AudioOutputDeviceInfo : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private bool _isSelected;

    public string Description => !string.IsNullOrEmpty(Subtitle)
        ? Subtitle
        : (IsDefault ? $"{Name} (Predeterminado en Windows)" : Name);
}
