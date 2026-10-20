using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;

namespace EngineSoundTool;

// WPF keeps selected items in hashed collections. Mutable value equality (a
// record including IsModified) changes their identity after a WAV replacement.
public sealed class BdlSection(int index, int pcmOffset, int sampleCount, float rpmMin, float rpmMax, int grainCount) : INotifyPropertyChanged
{
    public int Index { get; } = index;
    public int PcmOffset { get; } = pcmOffset;
    public int SampleCount { get; } = sampleCount;
    public float RpmMin { get; } = rpmMin;
    public float RpmMax { get; } = rpmMax;
    public int GrainCount { get; } = grainCount;
    private bool isModified;
    public bool IsModified
    {
        get => isModified;
        internal set
        {
            if (isModified == value) return;
            isModified = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public int PcmBytes => checked(SampleCount * 2);
    public double Seconds => SampleCount / 48000.0;
    public override string ToString() => $"Section {Index:00} · {Seconds:F3} s · {SampleCount:N0} Samples";
}

public sealed class BdlDocument
{
    readonly byte[] original;
    readonly Dictionary<int, byte[]> replacements = new();
    readonly object sync = new();
    private long changeRevision;
    public long ChangeRevision => Interlocked.Read(ref changeRevision);
    public string SourcePath { get; }
    public int Version { get; }
    public IReadOnlyList<BdlSection> Sections { get; }
    public bool IsDirty { get { lock(sync) return replacements.Count != 0; } }
    public int ChangeCount { get { lock(sync) return replacements.Count; } }
    public BdlDocument(string path) : this(path, File.ReadAllBytes(path)) { }
    public BdlDocument(string path, byte[] data)
    {
        SourcePath = Path.GetFullPath(path);
        original = (byte[])data.Clone();
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("bndl"u8)) throw new InvalidDataException("Not a BDL engine bundle.");
        Version = ReadInt(data, 4);
        if (Version is not (118 or 119)) throw new InvalidDataException($"BDL version {Version} is not supported.");
        var offsets = new List<int>();
        for (int i = 0; i <= data.Length - 0x554c; i++)
            if (data[i] == (byte)'e' && data.AsSpan(i, 4).SequenceEqual("egoe"u8) && ReadInt(data, i + 4) == Version) offsets.Add(i);
        if (offsets.Count == 0 || offsets[0] != 108) throw new InvalidDataException("Unknown BDL section structure.");
        var sections = new List<BdlSection>();
        for (int index = 0; index < offsets.Count; index++)
        {
            int start = offsets[index], limit = index + 1 < offsets.Count ? offsets[index + 1] : data.Length;
            if (ReadInt(data, start + 0x34) != 48000 || ReadInt(data, start + 0x44) != 21760) throw new InvalidDataException("Unknown PCM layout.");
            int count = ReadInt(data, start + 0x5548), pcm = start + 0x554c;
            long endLong = (long)pcm + (long)count * 2;
            if (count <= 0 || endLong + 4104 > limit) throw new InvalidDataException("Invalid PCM boundaries.");
            int end = (int)endLong;
            if (ReadInt(data, end) != 1024) throw new InvalidDataException("Invalid RPM table.");
            float min = float.MaxValue, max = float.MinValue;
            for (int n = 0; n < 1024; n++)
            {
                float rpm = BitConverter.Int32BitsToSingle(ReadInt(data, end + 4 + n * 4));
                if (!float.IsFinite(rpm) || rpm < 0 || rpm > 50000) throw new InvalidDataException("Invalid RPM value.");
                min = Math.Min(min, rpm); max = Math.Max(max, rpm);
            }
            int table = end + 4100, grains = ReadInt(data, table);
            if (grains <= 0 || grains >= 100000 || (long)table + 4 + (long)grains * 8 > limit) throw new InvalidDataException("Invalid position table.");
            double previous = 0;
            for (int n = 0; n < grains; n++)
            {
                double value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(table + 4 + n * 8, 8)));
                if (!double.IsFinite(value) || value < previous || value > count) throw new InvalidDataException("Invalid sample position.");
                previous = value;
            }
            sections.Add(new(index, pcm, count, min, max, grains));
        }
        Sections = sections;
    }
    static int ReadInt(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
    public bool IsReplaced(int index) { lock(sync) return replacements.ContainsKey(index); }
    public float[] GetRpmValues(int index)
    {
        var section = Sections[index]; int offset = section.PcmOffset + section.PcmBytes + 4;
        return Enumerable.Range(0,1024).Select(n => BitConverter.Int32BitsToSingle(ReadInt(original,offset+n*4))).ToArray();
    }
    public double[] GetPositions(int index)
    {
        var section = Sections[index]; int offset = section.PcmOffset + section.PcmBytes + 4104;
        return Enumerable.Range(0,section.GrainCount).Select(n => BinaryPrimitives.ReadDoubleLittleEndian(original.AsSpan(offset+n*8,8))).ToArray();
    }
    public byte[] GetPcm(int index)
    {
        var section = Sections[index];
        byte[]? pcm; lock(sync) replacements.TryGetValue(index, out pcm);
        return pcm is not null ? (byte[])pcm.Clone() : original.AsSpan(section.PcmOffset, section.PcmBytes).ToArray();
    }
    public void Replace(int index, byte[] wav)
    {
        byte[] pcm = PcmWave.Read(wav, Sections[index].SampleCount);
        var section = Sections[index];
        bool originalSamples = pcm.AsSpan().SequenceEqual(original.AsSpan(section.PcmOffset, section.PcmBytes));
        lock(sync)
        {
            if (originalSamples) replacements.Remove(index);
            else replacements[index] = pcm;
            section.IsModified = !originalSamples;
            Interlocked.Increment(ref changeRevision);
        }
    }
    public void Revert(int index) { lock(sync) { replacements.Remove(index); Sections[index].IsModified=false; Interlocked.Increment(ref changeRevision); } }
    public byte[] GetOriginal() => (byte[])original.Clone();
    public byte[] Build()
    {
        var result = (byte[])original.Clone();
        KeyValuePair<int,byte[]>[] changes; lock(sync) changes=replacements.ToArray();
        foreach (var (index, pcm) in changes) pcm.CopyTo(result, Sections[index].PcmOffset);
        _ = new BdlDocument(SourcePath, result);
        return result;
    }
    public void SaveAs(string target)
    {
        if (string.Equals(SourcePath, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) throw new IOException("The original file cannot be overwritten. Please choose a new name.");
        byte[] data = Build();
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(data);
    }
}

public static class PcmWave
{
    public static byte[] Encode(byte[] pcm)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(checked(pcm.Length + 36)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(48000); writer.Write(96000);
        writer.Write((ushort)2); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
        return stream.ToArray();
    }
    public static byte[] Read(byte[] wav, int samples)
    {
        const string format = "Required: uncompressed mono PCM16 WAV, 48,000 Hz, with exactly the displayed sample count.";
        if (wav.Length < 12 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException(format);
        long declared = (long)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4, 4)) + 8;
        if (declared != wav.Length) throw new InvalidDataException("Incomplete or inconsistent WAV file.");
        bool validFormat = false, seenFormat = false; byte[]? pcm = null;
        int offset = 12;
        while (offset < wav.Length)
        {
            if (wav.Length - offset < 8) throw new InvalidDataException("Incomplete WAV chunk.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset + 4, 4));
            long end = (long)offset + 8 + length;
            if (end > wav.Length) throw new InvalidDataException("Incomplete WAV chunk.");
            var id = wav.AsSpan(offset, 4); int body = offset + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (seenFormat || length < 16) throw new InvalidDataException(format);
                seenFormat = true;
                validFormat = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body, 2)) == 1 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2, 2)) == 1 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(body + 4, 4)) == 48000 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(body + 8, 4)) == 96000 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 12, 2)) == 2 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14, 2)) == 16;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (pcm != null) throw new InvalidDataException("Multiple WAV data chunks are not supported.");
                pcm = wav.AsSpan(body, (int)length).ToArray();
            }
            offset = checked((int)end + (int)(length % 2));
            if (offset > wav.Length) throw new InvalidDataException("Missing WAV padding.");
        }
        if (!validFormat || pcm == null) throw new InvalidDataException(format);
        if (pcm.Length != checked(samples * 2)) throw new InvalidDataException($"Incorrect length: exactly {samples:N0} samples ({samples * 2:N0} PCM bytes) are required. " + format);
        return pcm;
    }
}
