using System.Collections.ObjectModel;
using BenyComes.Models;
using BenyComes.Services.Audio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class AudioDeviceSelectorViewModel : ObservableObject
{
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    public ObservableCollection<AudioOutputDeviceInfo> Devices { get; } = new();

    [ObservableProperty]
    private AudioOutputDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private bool _isExclusiveMode;

    [ObservableProperty]
    private string _statusDescription = string.Empty;

    public AudioDeviceSelectorViewModel()
    {
        _isExclusiveMode = _audio.IsExclusiveMode;
        _audio.AudioModeChanged += (s, m) =>
        {
            IsExclusiveMode = m;
            UpdateStatusDescription();
        };

        _audio.OutputDeviceChanged += (s, d) => UpdateSelectedDeviceStatus();

        RefreshDevices();
        UpdateStatusDescription();
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        Devices.Clear();
        var list = _audio.GetAvailableOutputDevices();
        foreach (var dev in list)
        {
            Devices.Add(dev);
            if (dev.IsSelected)
            {
                SelectedDevice = dev;
            }
        }

        UpdateStatusDescription();
    }

    [RelayCommand]
    private void SelectDevice(AudioOutputDeviceInfo? device)
    {
        if (device == null) return;
        SelectedDevice = device;
        _audio.SelectedDeviceId = device.Id;
        UpdateSelectedDeviceStatus();
    }

    partial void OnIsExclusiveModeChanged(bool value)
    {
        _audio.IsExclusiveMode = value;
        UpdateStatusDescription();
    }

    private void UpdateSelectedDeviceStatus()
    {
        var currentId = _audio.SelectedDeviceId;
        foreach (var d in Devices)
        {
            d.IsSelected = (d.Id == currentId);
        }
    }

    private void UpdateStatusDescription()
    {
        if (IsExclusiveMode)
        {
            StatusDescription = "WASAPI Exclusive (Bit-Perfect): El reproductor toma control exclusivo del hardware. Se desactiva el mezclador de Windows y el volumen por software para enviar el flujo PCM puro directamente al DAC sin resampling.";
        }
        else
        {
            StatusDescription = "WASAPI Shared: Salida estándar integrada con el mezclador de Windows. Permite reproducir audio de varias aplicaciones simultáneamente y controlar el volumen por software.";
        }
    }
}
