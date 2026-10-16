using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

namespace BenyComes.Services.Audio;

public static class AudioDecoderFactory
{
    public static WaveStream CreateReader(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Audio file not found", filePath);
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            switch (ext)
            {
                case ".ogg":
                    return new VorbisWaveReader(filePath);

                case ".flac":
                    // MediaFoundationReader on Windows 10/11 decodes lossless FLAC 16/24-bit bit-perfect
                    try
                    {
                        return new MediaFoundationReader(filePath);
                    }
                    catch
                    {
                        return new AudioFileReader(filePath);
                    }

                case ".wav":
                    try
                    {
                        return new WaveFileReader(filePath);
                    }
                    catch
                    {
                        return new AudioFileReader(filePath);
                    }

                case ".mp3":
                case ".m4a":
                case ".aac":
                case ".wma":
                default:
                    try
                    {
                        return new AudioFileReader(filePath);
                    }
                    catch
                    {
                        return new MediaFoundationReader(filePath);
                    }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open {filePath}: {ex.Message}");
            // Final fallback attempt
            return new MediaFoundationReader(filePath);
        }
    }
}
