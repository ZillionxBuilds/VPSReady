using System.Text.Json;

namespace VpsReady.UnitTests;

/// <summary>Parses each physical line independently so test assertions also enforce JSONL.</summary>
internal static class JsonlTestEvidence
{
    public static JsonElement[] ReadRecords(string contents) =>
        contents.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            return document.RootElement.Clone();
        }).ToArray();
}
