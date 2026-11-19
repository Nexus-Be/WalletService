namespace Wallet.Application.Features.Holds;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Exceptions;

internal static class HoldOperations
{
    internal static string Fingerprint<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    internal static async Task<T?> Replay<T>(IWalletDbContext context, string key, string operation,
        string fingerprint, CancellationToken token) where T : class
    {
        var stored = await context.AuctionHoldOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == key, token);
        if (stored is null) return null;
        if (stored.Operation != operation || stored.RequestFingerprint != fingerprint)
            throw new BusinessRuleValidationException("IDEMPOTENCY_CONFLICT",
                "The idempotency key was already used for a different request.", 409);
        return JsonSerializer.Deserialize<T>(stored.ResponseJson)
            ?? throw new InvalidOperationException("Stored auction hold response is invalid.");
    }

    internal static void Record<T>(IWalletDbContext context, Guid holdId, string key,
        string operation, string fingerprint, T response, int status) =>
        context.AuctionHoldOperations.Add(new AuctionHoldOperation
        {
            Id = Guid.NewGuid(), HoldId = holdId, IdempotencyKey = key, Operation = operation,
            RequestFingerprint = fingerprint, ResponseJson = JsonSerializer.Serialize(response), HttpStatus = status
        });

    internal static string? ExtractAuctionId(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var metadata = JsonDocument.Parse(metadataJson);
            return metadata.RootElement.ValueKind == JsonValueKind.Object &&
                   metadata.RootElement.TryGetProperty("auctionId", out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string NormalizeMetadata(JsonElement? metadata)
    {
        if (metadata is null || metadata.Value.ValueKind == JsonValueKind.Null) return "{}";
        if (metadata.Value.ValueKind != JsonValueKind.Object)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Metadata must be an object.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, metadata.Value);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
            if (properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Metadata contains duplicate property names.");
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
