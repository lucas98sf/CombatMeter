using System.IO.Compression;
using System.Text.Json;

namespace CombatMeter.Shared;

// Private, bounded frames carried over the game's existing directed messages.
// No type names from the wire are reflected or instantiated.
public static class WireCodec
{
    public const string Prefix = "~CM4|";
    public const int ChunkSize = 360, MaxChunks = 256, MaxJsonBytes = 524288;
    public static IEnumerable<string> Encode<T>(string channel, long id, T packet)
    {
        var kind = packet switch {
            RegistrationPacket => 'R', HistoryRequestPacket => 'H', SourceRequestPacket => 'Q',
            PolicyPacket => 'P', EncounterCompletedPacket => 'F', EncounterPacket => 'E',
            SourceResponsePacket => 'S', _ => throw new ArgumentException("Unsupported packet") };
        var json = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (json.Length > MaxJsonBytes) throw new InvalidDataException("Packet exceeds JSON limit");
        using var output = new MemoryStream();
        using (var zip = new BrotliStream(output, CompressionLevel.Fastest, true)) zip.Write(json);
        var payload = Convert.ToBase64String(output.ToArray());
        var count = (payload.Length + ChunkSize - 1) / ChunkSize;
        if (count > MaxChunks) throw new InvalidDataException("Packet exceeds frame limit");
        for (var index = 0; index < count; index++)
            yield return $"{Prefix}{channel}|{id}|{kind}|{index}|{count}|{payload.Substring(index * ChunkSize, Math.Min(ChunkSize, payload.Length - index * ChunkSize))}";
    }

    public static object Decode(char kind, string encoded, int maxJsonBytes = MaxJsonBytes)
    {
        using var input = new MemoryStream(Convert.FromBase64String(encoded));
        using var zip = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = zip.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > maxJsonBytes) throw new InvalidDataException("Expanded packet exceeds limit");
            output.Write(buffer, 0, count);
        }
        var data = output.ToArray();
        return kind switch {
            'R' => JsonSerializer.Deserialize<RegistrationPacket>(data)!,
            'H' => JsonSerializer.Deserialize<HistoryRequestPacket>(data)!,
            'Q' => JsonSerializer.Deserialize<SourceRequestPacket>(data)!,
            'P' => JsonSerializer.Deserialize<PolicyPacket>(data)!,
            'E' => JsonSerializer.Deserialize<EncounterPacket>(data)!,
            'F' => JsonSerializer.Deserialize<EncounterCompletedPacket>(data)!,
            'S' => JsonSerializer.Deserialize<SourceResponsePacket>(data)!,
            _ => throw new InvalidDataException("Unknown packet kind") };
    }
}

public sealed class WireInbox
{
    private sealed record Transfer(char Kind, string[] Chunks, double Started);
    private readonly Dictionary<(string Channel, long Id), Transfer> _pending = new();
    private readonly HashSet<(string Channel, long Id)> _completed = new();
    private readonly Queue<(string Channel, long Id)> _order = new();
    public void Clear() { _pending.Clear(); _completed.Clear(); _order.Clear(); }
    public object? Receive(string frame, string? expectedChannel, bool serverbound, double now, out string channel)
    {
        channel = "";
        foreach (var key in _pending.Where(x => now - x.Value.Started > 5).Select(x => x.Key).ToArray()) _pending.Remove(key);
        if (frame.Length > 500 || !frame.StartsWith(WireCodec.Prefix, StringComparison.Ordinal)) return null;
        var parts = frame.Split('|');
        if (parts.Length != 7 || parts[1].Length != 32 || !Guid.TryParseExact(parts[1], "N", out _)
            || (expectedChannel != null && parts[1] != expectedChannel)
            || !long.TryParse(parts[2], out var id) || id <= 0 || parts[3].Length != 1
            || !int.TryParse(parts[4], out var index) || !int.TryParse(parts[5], out var count)
            || count < 1 || count > WireCodec.MaxChunks || index < 0 || index >= count || parts[6].Length > WireCodec.ChunkSize) return null;
        var kind = parts[3][0];
        if (!(serverbound ? "RHQ" : "PEFS").Contains(kind)) return null;
        // Requests are small, single-frame messages. Do not reserve large client-controlled transfers.
        if (serverbound && count != 1) return null;
        channel = parts[1];
        var keyId = (channel, id);
        if (_completed.Contains(keyId)) return null;
        if (!_pending.TryGetValue(keyId, out var transfer))
        {
            if (_pending.Count >= 4) return null;
            _pending[keyId] = transfer = new(kind, new string[count], now);
        }
        if (transfer.Kind != kind || transfer.Chunks.Length != count) { _pending.Remove(keyId); return null; }
        if (transfer.Chunks[index] is { } old && old != parts[6]) { _pending.Remove(keyId); return null; }
        transfer.Chunks[index] = parts[6];
        if (transfer.Chunks.Any(x => x == null)) return null;
        _pending.Remove(keyId);
        _completed.Add(keyId); _order.Enqueue(keyId);
        while (_order.Count > 128) _completed.Remove(_order.Dequeue());
        try { return WireCodec.Decode(kind, string.Concat(transfer.Chunks), serverbound ? 4096 : WireCodec.MaxJsonBytes); }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or JsonException or IOException) { return null; }
    }
}
