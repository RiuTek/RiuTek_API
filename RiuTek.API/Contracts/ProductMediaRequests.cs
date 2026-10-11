namespace RiuTek.API.Contracts;

public record CreateProductMediaUploadRequest(string FileName, string ContentType, long ContentLength);

public record ConfirmProductMediaUploadRequest(string ObjectKey);

public record DeleteProductMediaRequest(string ObjectKey);
