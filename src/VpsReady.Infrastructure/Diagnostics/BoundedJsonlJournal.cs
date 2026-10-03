using System.Text;
using System.Text.Json;

namespace VpsReady.Infrastructure.Diagnostics;

/// <summary>Normalizes legacy JSON streams and retains only whole, newest JSONL records.</summary>
internal static class BoundedJsonlJournal
{
    private static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, CompactOptions) + "\n";

    public static string Append(string existing, string appendedRecord, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var records = new Queue<(string Line, int Bytes)>();
        var retainedBytes = 0;
        ReadRecords(existing, rejectOversized: false);
        ReadRecords(appendedRecord, rejectOversized: true);
        return string.Concat(records.Select(record => record.Line));

        void ReadRecords(string contents, bool rejectOversized)
        {
            // Old versions wrote indented objects. Splitting physical lines would corrupt them.
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(contents), new JsonReaderOptions { AllowMultipleValues = true });
            try
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var document = JsonDocument.ParseValue(ref reader);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new IOException("A local journal record must be a JSON object.");
                    }

                    var line = Serialize(document.RootElement);
                    var bytes = Encoding.UTF8.GetByteCount(line);
                    if (rejectOversized && bytes > maximumBytes)
                    {
                        throw new IOException("A single sanitized journal event exceeds the local journal safety limit.");
                    }

                    records.Enqueue((line, bytes));
                    retainedBytes += bytes;
                    while (retainedBytes > maximumBytes)
                    {
                        retainedBytes -= records.Dequeue().Bytes;
                    }
                }
            }
            catch (JsonException)
            {
                // Parser exceptions can include journal content. Do not propagate that text.
                throw new IOException("The local journal contains an incomplete or invalid JSON record.");
            }
        }
    }
}
