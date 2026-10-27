using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Dsp;
using NAudio.Wave;

namespace BenyComes.Services.Audio;

public enum VisualizerMode
{
    None,
    SpectrumBars,
    Waveform,
    BassPulse
}

public partial class AudioVisualizerService : ObservableObject
{
    private static readonly Lazy<AudioVisualizerService> _lazy = new(() => new AudioVisualizerService());
    public static AudioVisualizerService Instance => _lazy.Value;

    private const int FftSize = 1024;
    private const int FftExponent = 10; // 2^10 = 1024
    private const int NumBars = 32;
    private const int WaveformPointsCount = 64;

    private readonly object _sampleLock = new();
    private readonly float[] _sampleBuffer = new float[FftSize * 2];
    private int _sampleBufferPos = 0;
    private DateTime _lastSampleTime = DateTime.MinValue;

    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly double[] _currentBarValues = new double[NumBars];
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private VisualizerMode _mode = VisualizerMode.SpectrumBars;

    [ObservableProperty]
    private string _selectedVisualizerMode = "Barras de Espectro";

    [ObservableProperty]
    private string _visualizerPlacement = "InfoCancion"; // "InfoCancion", "Centro", "InferiorDerecha"

    [ObservableProperty]
    private double _maxBarHeight = 50.0;

    [ObservableProperty]
    private double _backgroundDarkness = 0.70;

    [ObservableProperty]
    private double _bassScale = 1.0;

    [ObservableProperty]
    private double _bassGlowOpacity = 0.0;

    [ObservableProperty]
    private PointCollection _waveformPoints = new();

    public ObservableCollection<double> SpectrumBars { get; } = new();

    private AudioVisualizerService()
    {
        for (int i = 0; i < NumBars; i++)
        {
            SpectrumBars.Add(2.0);
            _currentBarValues[i] = 0.04;
        }

        InitDefaultWaveform();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(25) // ~40 FPS
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void InitDefaultWaveform()
    {
        var pc = new PointCollection(WaveformPointsCount);
        double step = 600.0 / (WaveformPointsCount - 1);
        for (int i = 0; i < WaveformPointsCount; i++)
        {
            pc.Add(new Point(i * step, 30.0));
        }
        WaveformPoints = pc;
    }

    partial void OnVisualizerPlacementChanged(string value)
    {
        // En centro y lateral derecho tiene más altura disponible (hasta 120-140px)
        MaxBarHeight = value switch
        {
            "Centro" => 120.0,
            "InferiorDerecha" => 100.0,
            _ => 50.0 // InfoCancion / Posición actual
        };
    }

    partial void OnSelectedVisualizerModeChanged(string value)
    {
        Mode = value switch
        {
            "Barras de Espectro" or "Barras de espectro" => VisualizerMode.SpectrumBars,
            "Onda Reactiva" or "Onda reactiva" => VisualizerMode.Waveform,
            "Pulso de Carátula" or "Pulso de Bajos" or "Pulso de bajos" => VisualizerMode.BassPulse,
            "Ninguno" => VisualizerMode.None,
            _ => VisualizerMode.SpectrumBars
        };
    }

    public void AddSamples(float[] buffer, int offset, int count)
    {
        if (count <= 0) return;

        lock (_sampleLock)
        {
            _lastSampleTime = DateTime.UtcNow;
            for (int i = 0; i < count; i++)
            {
                _sampleBuffer[_sampleBufferPos] = buffer[offset + i];
                _sampleBufferPos = (_sampleBufferPos + 1) % _sampleBuffer.Length;
            }
        }
    }

    public ISampleProvider Wrap(ISampleProvider source)
    {
        return new VisualizerSampleProvider(source, this);
    }

    public IWaveProvider Wrap(IWaveProvider source)
    {
        return new VisualizerWaveProvider(source, this);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        bool isSilent = (DateTime.UtcNow - _lastSampleTime).TotalMilliseconds > 350;

        if (isSilent || Mode == VisualizerMode.None)
        {
            DecayToRest();
            return;
        }

        float[] localSamples = new float[FftSize];
        lock (_sampleLock)
        {
            int start = (_sampleBufferPos - FftSize + _sampleBuffer.Length) % _sampleBuffer.Length;
            for (int i = 0; i < FftSize; i++)
            {
                localSamples[i] = _sampleBuffer[(start + i) % _sampleBuffer.Length];
            }
        }

        switch (Mode)
        {
            case VisualizerMode.SpectrumBars:
                ProcessSpectrumBars(localSamples);
                break;

            case VisualizerMode.Waveform:
                ProcessWaveform(localSamples);
                break;

            case VisualizerMode.BassPulse:
                ProcessBassPulse(localSamples);
                break;
        }
    }

    private void ProcessSpectrumBars(float[] samples)
    {
        // Apply Hanning Window & compute FFT
        for (int i = 0; i < FftSize; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
            _fftBuffer[i].X = (float)(samples[i] * window);
            _fftBuffer[i].Y = 0f;
        }

        FastFourierTransform.FFT(true, FftExponent, _fftBuffer);

        // Sample rate standard: 44.1kHz -> Nyquist 22050 Hz across 512 bins (~43 Hz per bin)
        // Usamos una distribución musical balanceada de frecuencias desde ~30 Hz hasta ~16000 Hz:
        int maxUsefulBin = Math.Min(FftSize / 2, 380); // ~16.3 kHz
        int minUsefulBin = 1; // ~43 Hz

        for (int b = 0; b < NumBars; b++)
        {
            // Curva perceptual exponencial suave para que cada barra represente un rango musical real
            double tStart = (double)b / NumBars;
            double tEnd = (double)(b + 1) / NumBars;

            // Escala logarítmica musical
            double minFreqIndex = minUsefulBin * Math.Pow((double)maxUsefulBin / minUsefulBin, tStart);
            double maxFreqIndex = minUsefulBin * Math.Pow((double)maxUsefulBin / minUsefulBin, tEnd);

            int startBin = Math.Clamp((int)minFreqIndex, 0, maxUsefulBin - 1);
            int endBin = Math.Clamp((int)Math.Ceiling(maxFreqIndex), startBin + 1, maxUsefulBin);

            double sum = 0;
            for (int k = startBin; k < endBin; k++)
            {
                double mag = Math.Sqrt(_fftBuffer[k].X * _fftBuffer[k].X + _fftBuffer[k].Y * _fftBuffer[k].Y);
                sum += mag;
            }

            double avg = sum / (endBin - startBin);

            // Ecualización de compensación acústica (Tilt / Treble Boost):
            // Las altas frecuencias tienen mucha menos energía acústica que los bajos en música mezclada/masterizada.
            // Aplicamos un boost progresivo desde los medios hasta los agudos (de 1.0x hasta 3.8x)
            double freqCompensation = 1.0 + Math.Pow((double)b / NumBars, 1.4) * 2.8;
            double compensatedAvg = avg * freqCompensation;

            // Rango dinámico y escalado perceptual
            double target = Math.Min(1.0, Math.Pow(compensatedAvg * 16.0, 0.75));

            // Smooth attack and decay
            if (target > _currentBarValues[b])
            {
                _currentBarValues[b] = _currentBarValues[b] * 0.22 + target * 0.78; // snappy attack
            }
            else
            {
                _currentBarValues[b] = _currentBarValues[b] * 0.80 + target * 0.20; // smooth decay
            }

            _currentBarValues[b] = Math.Clamp(_currentBarValues[b], 0.04, 1.0);
            if (b < SpectrumBars.Count)
            {
                SpectrumBars[b] = Math.Clamp(_currentBarValues[b] * MaxBarHeight, 2.0, MaxBarHeight);
            }
        }
    }

    private void ProcessWaveform(float[] samples)
    {
        var pc = new PointCollection(WaveformPointsCount);
        double stepX = 600.0 / (WaveformPointsCount - 1);
        int sampleStep = FftSize / WaveformPointsCount;

        for (int i = 0; i < WaveformPointsCount; i++)
        {
            int sIdx = Math.Min(samples.Length - 1, i * sampleStep);
            float sample = Math.Clamp(samples[sIdx], -1.0f, 1.0f);
            double y = 30.0 + (sample * 26.0); // centered around Y=30
            pc.Add(new Point(i * stepX, y));
        }

        WaveformPoints = pc;
    }

    private void ProcessBassPulse(float[] samples)
    {
        // FFT for bass energy in lowest 6 bins (~20 to 140 Hz)
        for (int i = 0; i < FftSize; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
            _fftBuffer[i].X = (float)(samples[i] * window);
            _fftBuffer[i].Y = 0f;
        }

        FastFourierTransform.FFT(true, FftExponent, _fftBuffer);

        double bassEnergy = 0;
        for (int k = 1; k <= 6; k++)
        {
            double mag = Math.Sqrt(_fftBuffer[k].X * _fftBuffer[k].X + _fftBuffer[k].Y * _fftBuffer[k].Y);
            bassEnergy += mag;
        }
        bassEnergy /= 6.0;

        double targetScale = 1.0 + Math.Min(0.08, bassEnergy * 0.16);
        double targetGlow = Math.Min(0.85, bassEnergy * 0.90);

        BassScale = BassScale * 0.65 + targetScale * 0.35;
        BassGlowOpacity = BassGlowOpacity * 0.70 + targetGlow * 0.30;
    }

    private void DecayToRest()
    {
        for (int i = 0; i < NumBars; i++)
        {
            _currentBarValues[i] = Math.Max(0.04, _currentBarValues[i] * 0.85);
            if (i < SpectrumBars.Count)
            {
                SpectrumBars[i] = Math.Max(2.0, _currentBarValues[i] * MaxBarHeight);
            }
        }

        BassScale = BassScale * 0.85 + 1.0 * 0.15;
        BassGlowOpacity = Math.Max(0.0, BassGlowOpacity * 0.80);
    }
}

public class VisualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly AudioVisualizerService _service;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public VisualizerSampleProvider(ISampleProvider source, AudioVisualizerService service)
    {
        _source = source;
        _service = service;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead > 0)
        {
            _service.AddSamples(buffer, offset, samplesRead);
        }
        return samplesRead;
    }
}

public class VisualizerWaveProvider : IWaveProvider
{
    private readonly IWaveProvider _source;
    private readonly AudioVisualizerService _service;
    private readonly float[] _tempFloatBuffer = new float[2048];

    public WaveFormat WaveFormat => _source.WaveFormat;

    public VisualizerWaveProvider(IWaveProvider source, AudioVisualizerService service)
    {
        _source = source;
        _service = service;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _source.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            // Convert bytes to float samples based on bit depth
            int bits = _source.WaveFormat.BitsPerSample;
            int floatCount = 0;

            if (bits == 16)
            {
                int sampleCount = bytesRead / 2;
                int maxSamples = Math.Min(sampleCount, _tempFloatBuffer.Length);
                for (int i = 0; i < maxSamples; i++)
                {
                    short sample = (short)(buffer[offset + i * 2] | (buffer[offset + i * 2 + 1] << 8));
                    _tempFloatBuffer[i] = sample / 32768f;
                }
                floatCount = maxSamples;
            }
            else if (bits == 32 && _source.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                int sampleCount = bytesRead / 4;
                int maxSamples = Math.Min(sampleCount, _tempFloatBuffer.Length);
                Buffer.BlockCopy(buffer, offset, _tempFloatBuffer, 0, maxSamples * 4);
                floatCount = maxSamples;
            }

            if (floatCount > 0)
            {
                _service.AddSamples(_tempFloatBuffer, 0, floatCount);
            }
        }
        return bytesRead;
    }
}
