using System.Buffers.Binary;
using System.Globalization;
using PaperNote.Core.Models;

namespace PaperNote.Core.Services;

public static class AudioWaveformService
{
    public const int DefaultPeakCount = 192;
    public const int MaximumStoredPeakCount = 2048;

    public static IReadOnlyList<float> ExtractWaveform(string filePath, int peakCount = DefaultPeakCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return ExtractWaveform(stream, peakCount);
    }

    public static IReadOnlyList<float> ExtractWaveform(Stream stream, int peakCount = DefaultPeakCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek) return [];
        peakCount = Math.Clamp(peakCount, 16, MaximumStoredPeakCount);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        stream.Position = 0;
        if (stream.Length < 44 || new string(reader.ReadChars(4)) != "RIFF") return [];
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") return [];

        ushort format = 0;
        ushort channels = 0;
        ushort bitsPerSample = 0;
        long dataOffset = 0;
        long dataLength = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkLength = reader.ReadUInt32();
            var chunkStart = stream.Position;
            if (chunkId == "fmt " && chunkLength >= 16)
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                dataOffset = stream.Position;
                dataLength = Math.Min(chunkLength, stream.Length - stream.Position);
            }
            var next = chunkStart + chunkLength + (chunkLength % 2);
            stream.Position = Math.Min(next, stream.Length);
        }

        if (dataOffset <= 0 || dataLength <= 0 || channels == 0 || bitsPerSample is not (8 or 16 or 24 or 32)) return [];
        if (format is not (1 or 3)) return [];
        var bytesPerSample = bitsPerSample / 8;
        var frameSize = bytesPerSample * channels;
        var frameCount = dataLength / frameSize;
        if (frameCount <= 0) return [];
        var bucketCount = (int)Math.Min(peakCount, frameCount);
        var peaks = new float[bucketCount];
        stream.Position = dataOffset;
        var frameBuffer = new byte[frameSize];
        for (long frame = 0; frame < frameCount; frame++)
        {
            if (reader.Read(frameBuffer, 0, frameBuffer.Length) != frameBuffer.Length) break;
            double max = 0;
            for (var channel = 0; channel < channels; channel++)
                max = Math.Max(max, ReadSample(frameBuffer.AsSpan(channel * bytesPerSample, bytesPerSample), format, bitsPerSample));
            var bucket = (int)Math.Min(bucketCount - 1, frame * bucketCount / frameCount);
            peaks[bucket] = Math.Max(peaks[bucket], (float)Math.Clamp(max, 0, 1));
        }
        Normalize(peaks);
        return peaks;
    }

    public static void AppendSample(AudioRecording recording, float amplitude)
    {
        ArgumentNullException.ThrowIfNull(recording);
        recording.WaveformPeaks ??= [];
        amplitude = float.IsFinite(amplitude) ? Math.Clamp(amplitude, 0, 1) : 0;
        recording.WaveformPeaks.Add(amplitude);
        if (recording.WaveformPeaks.Count <= MaximumStoredPeakCount) return;
        var compacted = new List<float>((recording.WaveformPeaks.Count + 1) / 2);
        for (var index = 0; index < recording.WaveformPeaks.Count; index += 2)
            compacted.Add(index + 1 < recording.WaveformPeaks.Count
                ? Math.Max(recording.WaveformPeaks[index], recording.WaveformPeaks[index + 1])
                : recording.WaveformPeaks[index]);
        recording.WaveformPeaks = compacted;
    }

    public static string FormatCompact(IReadOnlyList<float>? peaks, int width = 24, double progress = -1)
    {
        if (peaks is null || peaks.Count == 0) return "（暂无波形）";
        width = Math.Clamp(width, 8, 64);
        const string bars = "▁▂▃▄▅▆▇█";
        var chars = new char[width];
        for (var index = 0; index < width; index++)
        {
            var start = index * peaks.Count / width;
            var end = Math.Max(start + 1, (index + 1) * peaks.Count / width);
            var value = 0f;
            for (var sample = start; sample < Math.Min(end, peaks.Count); sample++) value = Math.Max(value, peaks[sample]);
            chars[index] = bars[(int)Math.Round(Math.Clamp(value, 0, 1) * (bars.Length - 1))];
        }
        if (progress >= 0)
        {
            var marker = Math.Clamp((int)Math.Round(Math.Clamp(progress, 0, 1) * (width - 1)), 0, width - 1);
            chars[marker] = '◆';
        }
        return new string(chars);
    }

    private static double ReadSample(ReadOnlySpan<byte> bytes, ushort format, ushort bitsPerSample)
    {
        if (format == 3 && bitsPerSample == 32)
        {
            var raw = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return Math.Abs(BitConverter.Int32BitsToSingle(raw));
        }
        return bitsPerSample switch
        {
            8 => Math.Abs((bytes[0] - 128) / 128d),
            16 => Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768d),
            24 => Math.Abs(ReadInt24(bytes) / 8388608d),
            32 => Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648d),
            _ => 0
        };
    }

    private static int ReadInt24(ReadOnlySpan<byte> bytes)
    {
        var value = bytes[0] | bytes[1] << 8 | bytes[2] << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private static void Normalize(float[] peaks)
    {
        var maximum = peaks.Length == 0 ? 0 : peaks.Max();
        if (maximum <= .0001f) return;
        for (var index = 0; index < peaks.Length; index++)
            peaks[index] = Math.Clamp((float)Math.Sqrt(peaks[index] / maximum), 0, 1);
    }
}
