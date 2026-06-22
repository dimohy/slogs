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
        var extension = GetSafeImageExtension(fileName, contentType);
        if (extension is null)
        {
            throw new InvalidOperationException("PNG, JPG, GIF, WebP 이미지만 업로드할 수 있습니다.");
        }

        if (imageLength <= 0 || imageLength > MaxImageBytes)
        {
            throw new InvalidOperationException("이미지는 5MB 이하만 업로드할 수 있습니다.");
        }

        var uploadRoot = GetUploadRoot();
        Directory.CreateDirectory(uploadRoot);

        var baseName = SanitizeFileBaseName(Path.GetFileNameWithoutExtension(fileName));
        var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{baseName}-{Guid.NewGuid():N}{extension}";
        var targetPath = Path.Combine(uploadRoot, storedFileName);

        await using (var target = File.Create(targetPath))
        {
            await imageStream.CopyToAsync(target, cancellationToken);
        }

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

    private static string? GetSafeImageExtension(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => extension == ".jpeg" ? ".jpeg" : ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" ? extension : null
        };
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
