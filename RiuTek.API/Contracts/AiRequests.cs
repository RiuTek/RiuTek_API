using RiuTek.Application.DTOs;

namespace RiuTek.API.Contracts;

public record AskAiRequest(
    string Question,
    string? ProductSlug = null,
    List<ChatMessageDto>? History = null,
    AiPreviousBuildDto? PreviousBuild = null);
