namespace RiuTek.API.Contracts;

public record CreateBannerRequest(
    string Title,
    string ImageObjectKey,
    string TargetUrl,
    int DisplayOrder = 0,
    bool IsActive = false);

public record UpdateBannerRequest(
    string Title,
    string ImageObjectKey,
    string TargetUrl,
    int DisplayOrder = 0,
    bool IsActive = false);
