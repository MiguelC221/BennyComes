using System;
using NAudio.Wave;

namespace BenyComes.Services.Audio;

/// <summary>
/// Bit-perfect PCM container adapter.
/// Adapts integer PCM bit-depth containers (e.g., 16-bit to 24-bit padded, 24-bit to 32-bit container, or 16-bit to 32-bit int)
/// required by USB DACs in WASAPI Exclusive mode WITHOUT any resampling, DSP, or frequency alteration.
/// </summary>
public class NativeAudioPcmAdapter : IWaveProvider
{
    private readonly WaveStream _sourceStream;
    private readonly WaveFormat _outputFormat;
    private readonly int _sourceBytesPerSample;
    private readonly int _targetBytesPerSample;
    private readonly byte[] _readBuffer;

    public WaveFormat WaveFormat => _outputFormat;

    public NativeAudioPcmAdapter(WaveStream sourceStream, WaveFormat targetHardwareFormat)
    {
        _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
        _outputFormat = targetHardwareFormat ?? throw new ArgumentNullException(nameof(targetHardwareFormat));

        _sourceBytesPerSample = _sourceStream.WaveFormat.BitsPerSample / 8;
        _targetBytesPerSample = _outputFormat.BitsPerSample / 8;

        // Ensure same sample rate & channels
        if (_sourceStream.WaveFormat.SampleRate != _outputFormat.SampleRate)
        {
            throw new InvalidOperationException("Bit-perfect adapter preserves original sample rate and cannot resample.");
        }

        if (_sourceStream.WaveFormat.Channels != _outputFormat.Channels)
        {
            throw new InvalidOperationException("Bit-perfect adapter preserves original channel count.");
        }

        _readBuffer = new byte[65536];
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        // If formats are identical, direct stream pass-through
        if (_sourceBytesPerSample == _targetBytesPerSample &&
            _sourceStream.WaveFormat.Encoding == _outputFormat.Encoding)
        {
            return _sourceStream.Read(buffer, offset, count);
        }

        // Target sample count
        int targetSamplesRequested = count / _targetBytesPerSample;
        int sourceBytesToRead = targetSamplesRequested * _sourceBytesPerSample;

        if (sourceBytesToRead > _readBuffer.Length)
        {
            sourceBytesToRead = (_readBuffer.Length / _sourceBytesPerSample) * _sourceBytesPerSample;
            targetSamplesRequested = sourceBytesToRead / _sourceBytesPerSample;
        }

        int bytesRead = _sourceStream.Read(_readBuffer, 0, sourceBytesToRead);
        if (bytesRead <= 0) return 0;

        int samplesRead = bytesRead / _sourceBytesPerSample;
        int destIdx = offset;

        if (_sourceBytesPerSample == 2 && _targetBytesPerSample == 4)
        {
            // 16-bit signed int to 32-bit int container (left-aligned 24/32 or standard 32)
            // Left shifting 16 bits gives full 32-bit integer range preserving bit precision
            for (int i = 0; i < samplesRead; i++)
            {
                short sample16 = (short)(_readBuffer[i * 2] | (_readBuffer[i * 2 + 1] << 8));
                int sample32 = sample16 << 16;

                buffer[destIdx++] = (byte)(sample32 & 0xFF);
                buffer[destIdx++] = (byte)((sample32 >> 8) & 0xFF);
                buffer[destIdx++] = (byte)((sample32 >> 16) & 0xFF);
                buffer[destIdx++] = (byte)((sample32 >> 24) & 0xFF);
            }
        }
        else if (_sourceBytesPerSample == 3 && _targetBytesPerSample == 4)
        {
            // 24-bit PCM into 32-bit container (left shifted by 8)
            for (int i = 0; i < samplesRead; i++)
            {
                int b0 = _readBuffer[i * 3];
                int b1 = _readBuffer[i * 3 + 1];
                int b2 = _readBuffer[i * 3 + 2];
                int sample24in32 = (b0 << 8) | (b1 << 16) | (b2 << 24);

                buffer[destIdx++] = (byte)(sample24in32 & 0xFF);
                buffer[destIdx++] = (byte)((sample24in32 >> 8) & 0xFF);
                buffer[destIdx++] = (byte)((sample24in32 >> 16) & 0xFF);
                buffer[destIdx++] = (byte)((sample24in32 >> 24) & 0xFF);
            }
        }
        else if (_sourceBytesPerSample == 2 && _targetBytesPerSample == 3)
        {
            // 16-bit to 24-bit container (left shifted by 8)
            for (int i = 0; i < samplesRead; i++)
            {
                byte b0 = _readBuffer[i * 2];
                byte b1 = _readBuffer[i * 2 + 1];

                buffer[destIdx++] = 0; // LSB
                buffer[destIdx++] = b0;
                buffer[destIdx++] = b1;
            }
        }
        else
        {
            // Direct copy fallback if counts align
            int copyBytes = Math.Min(bytesRead, count);
            Buffer.BlockCopy(_readBuffer, 0, buffer, offset, copyBytes);
            return copyBytes;
        }

        return destIdx - offset;
    }
}
