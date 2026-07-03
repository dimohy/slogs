using System.Buffers.Binary;

namespace Slogs.Data;

public sealed class EditorImageStorage(IWebHostEnvironment environment)
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    private const string UploadsPathPrefix = "/uploads/";

    public async Task<EditorImageResponse> SaveAsync(
        Stream imageStream,
        string fileName,
        string? contentType,
        long imageLength,
        CancellationToken cancellationToken = default)
    {
        var declaredExtension = GetDeclaredImageExtension(fileName, contentType);
        if (declaredExtension is null)
        {
            throw new InvalidOperationException("PNG, JPG, GIF, WebP 이미지만 업로드할 수 있습니다.");
        }

        if (imageLength <= 0 || imageLength > MaxImageBytes)
        {
            throw new InvalidOperationException("이미지는 5MB 이하만 업로드할 수 있습니다.");
        }

        await using var buffer = new MemoryStream(checked((int)imageLength));
        await imageStream.CopyToAsync(buffer, cancellationToken);
        var imageBytes = buffer.ToArray();
        if (imageBytes.LongLength != imageLength)
        {
            throw new InvalidOperationException("이미지 바이트 길이가 요청 정보와 일치하지 않습니다.");
        }

        var actualExtension = DetectImageExtension(imageBytes);
        if (actualExtension is null || !IsEquivalentImageExtension(declaredExtension, actualExtension))
        {
            throw new InvalidOperationException("이미지 데이터가 선언된 파일 형식과 일치하지 않거나 손상되었습니다.");
        }

        var uploadRoot = GetUploadRoot();
        Directory.CreateDirectory(uploadRoot);

        var baseName = SanitizeFileBaseName(Path.GetFileNameWithoutExtension(fileName));
        var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{baseName}-{Guid.NewGuid():N}{actualExtension}";
        var targetPath = Path.Combine(uploadRoot, storedFileName);

        await File.WriteAllBytesAsync(targetPath, imageBytes, cancellationToken);

        return new EditorImageResponse(
            $"/uploads/{storedFileName}",
            string.IsNullOrWhiteSpace(baseName) ? "image" : baseName);
    }

    public Task<bool> DeleteUploadAsync(string url)
    {
        var normalizedUrl = NormalizeUploadUrl(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return Task.FromResult(false);
        }

        var fileName = normalizedUrl[UploadsPathPrefix.Length..];
        var uploadRoot = GetUploadRoot();
        var targetPath = Path.GetFullPath(Path.Combine(uploadRoot, fileName));
        var rootPath = Path.GetFullPath(uploadRoot);
        if (!targetPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("업로드 이미지 경로가 올바르지 않습니다.");
        }

        if (!File.Exists(targetPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(targetPath);
        return Task.FromResult(true);
    }

    public static string NormalizeUploadUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string path;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }
        else
        {
            var queryIndex = trimmed.IndexOfAny(['?', '#']);
            path = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
        }

        if (!path.StartsWith(UploadsPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(path, UploadsPathPrefix + fileName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return UploadsPathPrefix + fileName;
    }

    private string GetUploadRoot()
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        return Path.Combine(webRoot, "uploads");
    }

    private static string? GetDeclaredImageExtension(string fileName, string? contentType)
    {
        var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim().ToLowerInvariant();
        var contentTypeExtension = normalizedContentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => null
        };
        if (contentTypeExtension is not null)
        {
            return contentTypeExtension;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"
            ? extension
            : null;
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> bytes)
    {
        if (IsPng(bytes))
        {
            return ".png";
        }

        if (IsJpeg(bytes))
        {
            return ".jpg";
        }

        if (IsGif(bytes))
        {
            return ".gif";
        }

        return IsWebp(bytes) ? ".webp" : null;
    }

    private static bool IsEquivalentImageExtension(string declaredExtension, string actualExtension)
    {
        var normalizedDeclared = declaredExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? ".jpg"
            : declaredExtension.ToLowerInvariant();
        return normalizedDeclared.Equals(actualExtension, StringComparison.Ordinal);
    }

    private static bool IsPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 || !bytes[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        var offset = 8;
        var isFirstChunk = true;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..(offset + 4)]);
            if (length > int.MaxValue)
            {
                return false;
            }

            offset += 4;
            var chunkType = bytes[offset..(offset + 4)];
            offset += 4;
            if (isFirstChunk)
            {
                if (length != 13 || !chunkType.SequenceEqual("IHDR"u8))
                {
                    return false;
                }

                isFirstChunk = false;
            }

            var chunkLength = (int)length;
            if (offset + chunkLength + 4 > bytes.Length)
            {
                return false;
            }

            offset += chunkLength;
            offset += 4;

            if (chunkType.SequenceEqual("IEND"u8))
            {
                return length == 0 && offset == bytes.Length;
            }
        }

        return false;
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool IsJpeg(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 4
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[^2] == 0xFF
            && bytes[^1] == 0xD9;

    private static bool IsGif(ReadOnlySpan<byte> bytes)
        => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8);

    private static bool IsWebp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var riffLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]);
        if (riffLength != (uint)(bytes.Length - 8))
        {
            return false;
        }

        var format = bytes[12..16];
        return format.SequenceEqual("VP8 "u8)
            || format.SequenceEqual("VP8L"u8)
            || format.SequenceEqual("VP8X"u8);
    }

    private static string SanitizeFileBaseName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "image" : value.Trim();
        Span<char> buffer = stackalloc char[Math.Min(source.Length, 32)];
        var length = 0;

        foreach (var character in source)
        {
            if (length >= buffer.Length)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
            else if (character is '-' or '_')
            {
                buffer[length++] = character;
            }
        }

        return length == 0 ? "image" : new string(buffer[..length]);
    }
}
