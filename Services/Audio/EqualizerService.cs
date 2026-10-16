using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Dsp;
using NAudio.Wave;

namespace BenyComes.Services.Audio;

public partial class EqualizerBand : ObservableObject
{
    public string Label { get; init; } = string.Empty;
    public float Frequency { get; init; }
    public float Bandwidth { get; init; } = 1.0f;

    [ObservableProperty]
    private float _gain;

    public string GainFormatted => Gain >= 0 ? $"+{Gain:0.0} dB" : $"{Gain:0.0} dB";

    public string DisplayFrequency => Label.EndsWith("k", StringComparison.OrdinalIgnoreCase) ? $"{Label}Hz" : $"{Label} Hz";

    public event Action? GainChanged;

    partial void OnGainChanged(float value)
    {
        OnPropertyChanged(nameof(GainFormatted));
        GainChanged?.Invoke();
    }
}

public partial class EqualizerService : ObservableObject
{
    private static readonly Lazy<EqualizerService> _lazy = new(() => new EqualizerService());
    public static EqualizerService Instance => _lazy.Value;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private float _preampDb = 0.0f;

    [ObservableProperty]
    private float _bassBoostDb = 0.0f;

    [ObservableProperty]
    private float _stereoWidth = 1.0f;

    [ObservableProperty]
    private string _selectedPreset = "Plano (Bit-Neutral)";

    public string PreampFormatted => PreampDb >= 0 ? $"+{PreampDb:0.0} dB" : $"{PreampDb:0.0} dB";
    public string BassBoostFormatted => $"+{BassBoostDb:0.0} dB";
    public string StereoWidthFormatted => $"{(int)(StereoWidth * 100)}%";

    public ObservableCollection<EqualizerBand> Bands { get; } = new();

    public List<string> Presets { get; } = new()
    {
        "Plano (Bit-Neutral)",
        "Refuerzo de Graves (Bass Boost)",
        "Rock & Metal",
        "Electrónica & EDM",
        "Vocal & Acústico",
        "Clásica & Orquestal",
        "V-Shape Hi-Fi"
    };

    private bool _suppressPresetSync;
    public event Action? FiltersUpdated;

    private EqualizerService()
    {
        float[] freqs = { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
        string[] labels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

        for (int i = 0; i < freqs.Length; i++)
        {
            var band = new EqualizerBand
            {
                Frequency = freqs[i],
                Label = labels[i],
                Bandwidth = 0.9f,
                Gain = 0f
            };
            band.GainChanged += () =>
            {
                FiltersUpdated?.Invoke();
            };
            Bands.Add(band);
        }
    }

    partial void OnIsEnabledChanged(bool value) => FiltersUpdated?.Invoke();
    partial void OnPreampDbChanged(float value)
    {
        OnPropertyChanged(nameof(PreampFormatted));
        FiltersUpdated?.Invoke();
    }
    partial void OnBassBoostDbChanged(float value)
    {
        OnPropertyChanged(nameof(BassBoostFormatted));
        FiltersUpdated?.Invoke();
    }
    partial void OnStereoWidthChanged(float value)
    {
        OnPropertyChanged(nameof(StereoWidthFormatted));
        FiltersUpdated?.Invoke();
    }

    partial void OnSelectedPresetChanged(string value)
    {
        if (_suppressPresetSync) return;
        ApplyPreset(value);
    }

    [RelayCommand]
    public void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return;
        _suppressPresetSync = true;
        SelectedPreset = presetName;

        float[] gains = presetName switch
        {
            "Refuerzo de Graves (Bass Boost)" => new[] { 6.5f, 5.5f, 4.0f, 2.0f, 0.5f, 0.0f, 0.0f, 0.5f, 1.0f, 1.5f },
            "Rock & Metal" => new[] { 4.5f, 3.5f, 2.0f, -1.0f, -2.0f, -0.5f, 2.0f, 3.5f, 4.5f, 4.5f },
            "Electrónica & EDM" => new[] { 5.5f, 4.5f, 1.5f, 0.0f, -1.5f, 1.5f, 1.0f, 2.5f, 4.5f, 5.0f },
            "Vocal & Acústico" => new[] { -1.5f, -1.0f, 0.5f, 2.5f, 4.0f, 4.0f, 3.0f, 1.5f, 0.5f, 0.0f },
            "Clásica & Orquestal" => new[] { 3.5f, 2.5f, 1.5f, 0.5f, -0.5f, -0.5f, 0.5f, 2.0f, 3.0f, 4.0f },
            "V-Shape Hi-Fi" => new[] { 4.0f, 3.5f, 1.5f, -1.0f, -2.0f, -1.5f, 1.0f, 2.5f, 4.0f, 5.0f },
            _ => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }
        };

        for (int i = 0; i < Math.Min(Bands.Count, gains.Length); i++)
        {
            Bands[i].Gain = gains[i];
        }

        _suppressPresetSync = false;
        FiltersUpdated?.Invoke();
    }

    [RelayCommand]
    public void ResetEqualizer()
    {
        PreampDb = 0f;
        BassBoostDb = 0f;
        StereoWidth = 1.0f;
        ApplyPreset("Plano (Bit-Neutral)");
    }

    public ISampleProvider Wrap(ISampleProvider source)
    {
        return new EqualizerSampleProvider(source, this);
    }

    private sealed class EqualizerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EqualizerService _eq;
        private readonly BiQuadFilter[,] _filters;
        private readonly BiQuadFilter[] _bassShelf;
        private readonly int _channels;
        private readonly int _sampleRate;
        private bool _dirty = true;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EqualizerSampleProvider(ISampleProvider source, EqualizerService eq)
        {
            _source = source;
            _eq = eq;
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;
            _filters = new BiQuadFilter[_channels, eq.Bands.Count];
            _bassShelf = new BiQuadFilter[_channels];

            RebuildFilters();
            _eq.FiltersUpdated += () => _dirty = true;
        }

        private void RebuildFilters()
        {
            int bandCount = _eq.Bands.Count;
            for (int ch = 0; ch < _channels; ch++)
            {
                for (int b = 0; b < bandCount; b++)
                {
                    var band = _eq.Bands[b];
                    float freq = Math.Min(band.Frequency, _sampleRate * 0.45f);
                    _filters[ch, b] = BiQuadFilter.PeakingEQ(_sampleRate, freq, band.Bandwidth, band.Gain);
                }
                _bassShelf[ch] = BiQuadFilter.LowShelf(_sampleRate, 80f, 0.707f, _eq.BassBoostDb);
            }
            _dirty = false;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            if (!_eq.IsEnabled || samplesRead == 0) return samplesRead;

            if (_dirty)
            {
                RebuildFilters();
            }

            float preampLinear = MathF.Pow(10f, _eq.PreampDb / 20f);
            int bandCount = _eq.Bands.Count;
            bool hasBass = MathF.Abs(_eq.BassBoostDb) > 0.05f;
            float width = _eq.StereoWidth;

            for (int n = 0; n < samplesRead; n++)
            {
                int ch = n % _channels;
                int idx = offset + n;
                float sample = buffer[idx] * preampLinear;

                if (hasBass)
                {
                    sample = _bassShelf[ch].Transform(sample);
                }

                for (int b = 0; b < bandCount; b++)
                {
                    if (MathF.Abs(_eq.Bands[b].Gain) > 0.05f)
                    {
                        sample = _filters[ch, b].Transform(sample);
                    }
                }

                buffer[idx] = Math.Clamp(sample, -1.0f, 1.0f);
            }

            // Optional Stereo Width DSP when 2 channels
            if (_channels == 2 && MathF.Abs(width - 1.0f) > 0.02f)
            {
                for (int n = 0; n < samplesRead - 1; n += 2)
                {
                    float l = buffer[offset + n];
                    float r = buffer[offset + n + 1];
                    float mid = (l + r) * 0.5f;
                    float side = (l - r) * 0.5f * width;
                    buffer[offset + n] = Math.Clamp(mid + side, -1.0f, 1.0f);
                    buffer[offset + n + 1] = Math.Clamp(mid - side, -1.0f, 1.0f);
                }
            }

            return samplesRead;
        }
    }
}
