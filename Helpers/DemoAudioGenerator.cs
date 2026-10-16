using System;
using System.IO;
using System.Threading.Tasks;
using BenyComes.Models;
using BenyComes.Services.Metadata;
using NAudio.Wave;

namespace BenyComes.Helpers;

public static class DemoAudioGenerator
{
    public static async Task<Track> GenerateHiResSampleTrackAsync(string fileName, string title, string artist, string album, int sampleRate, int bitsPerSample, double durationSecs)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var demoDir = Path.Combine(appData, "BenyComes", "DemoTracks");
        Directory.CreateDirectory(demoDir);

        var filePath = Path.Combine(demoDir, fileName);

        if (!File.Exists(filePath))
        {
            await Task.Run(() =>
            {
                var waveFormat = bitsPerSample == 24
                    ? new WaveFormat(sampleRate, 24, 2)
                    : new WaveFormat(sampleRate, 16, 2);

                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new WaveFileWriter(fileStream, waveFormat);

                // Synthesize a soothing rich harmonic chord progression (A4 - F#m - D - E)
                // using harmonic overtones for high fidelity acoustic warmth
                double[] chordFreqs = { 440.0, 554.37, 659.25 }; // A Major chord
                double totalSamples = sampleRate * durationSecs;
                int bufferLength = 4096;
                byte[] buffer = new byte[bufferLength * (bitsPerSample / 8) * 2];

                int currentSample = 0;
                while (currentSample < totalSamples)
                {
                    int samplesThisChunk = Math.Min(bufferLength, (int)(totalSamples - currentSample));
                    int byteIdx = 0;

                    for (int s = 0; s < samplesThisChunk; s++)
                    {
                        double t = (double)(currentSample + s) / sampleRate;

                        // Envelope: gentle fade in & fade out
                        double env = 1.0;
                        if (t < 1.0) env = t;
                        else if (t > durationSecs - 1.5) env = Math.Max(0.0, (durationSecs - t) / 1.5);

                        // Complex musical chord with harmonics
                        double sampleLeft = 0;
                        double sampleRight = 0;

                        // Modulating ambient wave
                        double chord1 = Math.Sin(2 * Math.PI * 220.0 * t) * 0.35 +
                                        Math.Sin(2 * Math.PI * 440.0 * t) * 0.25 +
                                        Math.Sin(2 * Math.PI * 554.37 * t) * 0.20 +
                                        Math.Sin(2 * Math.PI * 659.25 * t) * 0.15;

                        // Subtle stereo panning vibrato
                        double panL = 0.5 + 0.2 * Math.Sin(2 * Math.PI * 0.2 * t);
                        double panR = 1.0 - panL;

                        sampleLeft = chord1 * env * panL * 0.65;
                        sampleRight = chord1 * env * panR * 0.65;

                        if (bitsPerSample == 24)
                        {
                            int valL = (int)(sampleLeft * 8388607.0);
                            int valR = (int)(sampleRight * 8388607.0);

                            buffer[byteIdx++] = (byte)(valL & 0xFF);
                            buffer[byteIdx++] = (byte)((valL >> 8) & 0xFF);
                            buffer[byteIdx++] = (byte)((valL >> 16) & 0xFF);

                            buffer[byteIdx++] = (byte)(valR & 0xFF);
                            buffer[byteIdx++] = (byte)((valR >> 8) & 0xFF);
                            buffer[byteIdx++] = (byte)((valR >> 16) & 0xFF);
                        }
                        else
                        {
                            short valL = (short)(sampleLeft * 32767.0);
                            short valR = (short)(sampleRight * 32767.0);

                            buffer[byteIdx++] = (byte)(valL & 0xFF);
                            buffer[byteIdx++] = (byte)((valL >> 8) & 0xFF);

                            buffer[byteIdx++] = (byte)(valR & 0xFF);
                            buffer[byteIdx++] = (byte)((valR >> 8) & 0xFF);
                        }
                    }

                    writer.Write(buffer, 0, samplesThisChunk * (bitsPerSample / 8) * 2);
                    currentSample += samplesThisChunk;
                }
            });
        }

        var track = await MetadataService.Instance.LoadTrackMetadataAsync(filePath);
        track.Title = title;
        track.Artist = artist;
        track.Album = album;
        track.Duration = TimeSpan.FromSeconds(durationSecs);
        track.SampleRate = sampleRate;
        track.BitsPerSample = bitsPerSample;
        track.Channels = 2;
        track.FileFormat = bitsPerSample == 24 ? "WAV (Hi-Res)" : "WAV";

        return track;
    }
}
