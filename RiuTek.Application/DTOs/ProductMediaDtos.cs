namespace RiuTek.Application.DTOs;

public record ProductMediaUploadDto(
    string ObjectKey,
    string UploadUrl,
    string PublicUrl,
    DateTimeOffset ExpiresAt,
    string HttpMethod,
    IReadOnlyDictionary<string, string> RequiredHeaders);

public record ProductMediaDto(
    string ObjectKey,
    string PublicUrl,
    string ContentType,
    long ContentLength,
    string? ETag);

public record ProductMediaMetadata(
    string ObjectKey,
    string ContentType,
    long ContentLength,
    string? ETag);
