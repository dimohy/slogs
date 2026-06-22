using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class SlogsPostMcpTools(
    IHttpContextAccessor httpContextAccessor,
    BlogService blogService,
    EditorImageStorage imageStorage,
    PostImageService postImageService)
{
    [McpServerTool(Name = "slogs_post_upload_image")]
    [Description("Upload an image for Slogs post Markdown and return a /uploads URL. Use this before save/update/publish when an image is local or generated. This tool accepts image bytes as base64, not local file paths. Uploaded images must be included in the saved Markdown body, otherwise the next post save/update treats them as unused and deletes them.")]
    public async Task<string> UploadImageAsync(
        [Description("Base64-encoded image bytes. A data URL like data:image/png;base64,... is also accepted. Do not pass local file paths or external URLs here.")] string base64Image,
        [Description("Original file name including extension. Required when base64Image is not a data URL with a supported content type.")] string fileName = "image.png",
        [Description("Image content type such as image/png, image/jpeg, image/gif, or image/webp. Optional when base64Image is a data URL or fileName has a supported extension.")] string? contentType = null)
    {
        var user = RequireUser();
        var image = DecodeImage(base64Image, contentType);
        using var imageStream = new MemoryStream(image.Bytes);
        var response = await imageStorage.SaveAsync(
            imageStream,
            fileName,
            image.ContentType,
            image.Bytes.Length);
        await postImageService.RegisterUploadAsync(user.UserName, response.Url);

        return FormatUploadedImageMarkdown(response);
    }

    [McpServerTool(Name = "slogs_post_save_draft")]
    [Description("Save a Markdown Slogs post before publishing for the authenticated user. Pre-publish posts are visible only to the owner and are not publicly listed. If slug is provided, the owned pre-publish post is updated and kept pre-publish.")]
    public async Task<string> SaveDraftAsync(
        [Description("Optional owned pre-publish post slug to update. Omit to create a new pre-publish post.")] string? slug = null,
        [Description("Optional URL slug for a new or pre-publish post. Korean is supported. If omitted, Slogs derives it from the title.")] string? customSlug = null,
        [Description("Pre-publish title. Required when creating a pre-publish post unless markdown is provided.")] string? title = null,
        [Description("Markdown body. Required when creating a pre-publish post unless title is provided. For local or generated images, call slogs_post_upload_image first and use the returned /uploads URL in Markdown. Uploaded images not included in this Markdown are deleted as unused on save.")] string? markdown = null,
        [Description("Optional post summary. If omitted on create, Slogs derives a short summary from the Markdown body.")] string? summary = null,
        [Description("Optional comma-separated tags. Slogs keeps up to five tags. Leave empty for no tags.")] string? tags = null,
        [Description("Optional series name.")] string? series = null,
        [Description("Optional representative image URL. Use http, https, or /uploads paths only. For local or generated images, call slogs_post_upload_image first. If omitted, Slogs uses the first Markdown body image when present.")] string? thumbnailUrl = null)
    {
        var user = RequireUser();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var existing = await RequireOwnedPostAsync(slug, user);
            var post = await UpdateOwnedPostAsync(existing, user, title, markdown, summary, tags, series, thumbnailUrl, isDraft: true, customSlug: customSlug);
            return FormatDraftSavedPostMarkdown(post, BuildEditUrl(post));
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Slogs 게시전 저장에는 제목이나 Markdown 본문 중 하나가 필요합니다.");
        }

        var draft = await blogService.CreatePostAsync(
            title ?? string.Empty,
            user.UserName,
            summary ?? string.Empty,
            markdown ?? string.Empty,
            tags ?? string.Empty,
            series,
            thumbnailUrl,
            isDraft: true,
            slug: customSlug);

        return FormatDraftSavedPostMarkdown(draft, BuildEditUrl(draft));
    }

    [McpServerTool(Name = "slogs_post_update")]
    [Description("Update an owned pre-publish Markdown Slogs post, or publish a new revision when the post is already published.")]
    public async Task<string> UpdateAsync(
        [Description("Owned Slogs post slug to update.")] string slug,
        [Description("Optional URL slug for an owned pre-publish post. Korean is supported. Ignored for already published posts so public URLs stay stable.")] string? customSlug = null,
        [Description("Optional replacement title. Omitted fields keep their current values.")] string? title = null,
        [Description("Optional replacement Markdown body. Omitted fields keep their current values. For local or generated images, call slogs_post_upload_image first and use the returned /uploads URL in Markdown. Uploaded images removed from this Markdown are deleted as unused on update.")] string? markdown = null,
        [Description("Optional replacement summary. Omitted fields keep their current values.")] string? summary = null,
        [Description("Optional replacement comma-separated tags. Omitted fields keep their current values; pass an empty string to clear tags.")] string? tags = null,
        [Description("Optional replacement series name. Omitted fields keep their current values.")] string? series = null,
        [Description("Optional replacement representative image URL. Omitted fields keep their current values. For local or generated images, call slogs_post_upload_image first. If the resulting value is empty, Slogs uses the first Markdown body image when present.")] string? thumbnailUrl = null)
    {
        var user = RequireUser();
        var existing = await RequireOwnedPostAsync(slug, user);
        var post = await UpdateOwnedPostAsync(existing, user, title, markdown, summary, tags, series, thumbnailUrl, isDraft: null, customSlug: customSlug);
        return FormatUpdatedPostMarkdown(post, BuildPublicPostUrl(post), BuildEditUrl(post));
    }

    [McpServerTool(Name = "slogs_post_publish")]
    [Description("Publish a Markdown Slogs post for the authenticated user. If slug is a pre-publish post, publish it. If slug is already published, create a new revision. If slug is omitted, create a new published site post. This creates normal Slogs posts, not LLM Wiki memories.")]
    public async Task<string> PublishAsync(
        [Description("Optional owned pre-publish or published post slug. Omit to create and publish a new post.")] string? slug = null,
        [Description("Optional URL slug for a new post or an owned pre-publish post being published. Korean is supported. Ignored for already published posts so public URLs stay stable.")] string? customSlug = null,
        [Description("Public post title. Required when slug is omitted. Optional replacement when slug is provided.")] string? title = null,
        [Description("Markdown body to publish as Slogs post content. Required when slug is omitted. Optional replacement when slug is provided. For local or generated images, call slogs_post_upload_image first and use the returned /uploads URL in Markdown. Uploaded images not included in this Markdown are deleted as unused on publish.")] string? markdown = null,
        [Description("Optional post summary. If omitted on create, Slogs derives a short summary from the Markdown body.")] string? summary = null,
        [Description("Optional comma-separated tags. Slogs keeps up to five tags. Leave empty for no tags.")] string? tags = null,
        [Description("Optional series name.")] string? series = null,
        [Description("Optional representative image URL. Use http, https, or /uploads paths only. For local or generated images, call slogs_post_upload_image first. If omitted, Slogs uses the first Markdown body image when present.")] string? thumbnailUrl = null)
    {
        var user = RequireUser();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var existing = await RequireOwnedPostAsync(slug, user);
            var publishedExistingPost = await UpdateOwnedPostAsync(existing, user, title, markdown, summary, tags, series, thumbnailUrl, isDraft: false, customSlug: customSlug);
            return existing.IsDraft
                ? FormatPublishedPostMarkdown(publishedExistingPost, BuildPublicPostUrl(publishedExistingPost))
                : FormatUpdatedPostMarkdown(publishedExistingPost, BuildPublicPostUrl(publishedExistingPost), BuildEditUrl(publishedExistingPost));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Slogs 게시에는 제목이 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Slogs 게시에는 Markdown 본문이 필요합니다.");
        }

        var publishedPost = await blogService.CreatePostAsync(
            title,
            user.UserName,
            summary ?? string.Empty,
            markdown,
            tags ?? string.Empty,
            series,
            thumbnailUrl,
            isDraft: false,
            slug: customSlug);

        return FormatPublishedPostMarkdown(publishedPost, BuildPublicPostUrl(publishedPost));
    }

    [McpServerTool(Name = "slogs_post_read")]
    [Description("Read an owned or public Slogs post by slug. This reads normal Slogs posts, including the authenticated user's pre-publish posts, not LLM Wiki memories.")]
    public async Task<string> ReadAsync(
        [Description("Post slug returned by Slogs post MCP tools or visible in a Slogs post URL.")] string slug)
    {
        var user = RequireUser();
        var post = await blogService.GetBySlugAsync(slug, user.UserName);
        return post is null
            ? "Slogs post not found."
            : FormatPostMarkdown(post, BuildPublicPostUrl(post), BuildEditUrl(post));
    }

    [McpServerTool(Name = "slogs_post_delete")]
    [Description("Delete an owned Slogs post by slug. Deletion uses the same BlogService path as the site UI, including tracked post image cleanup.")]
    public async Task<string> DeleteAsync(
        [Description("Owned Slogs post slug to delete.")] string slug)
    {
        var user = RequireUser();
        var existing = await RequireOwnedPostAsync(slug, user);
        var publicUrl = BuildPublicPostUrl(existing);
        var deleted = await blogService.DeletePostAsync(slug, user.UserName);
        if (!deleted)
        {
            throw new InvalidOperationException("Slogs 글 삭제에 실패했습니다.");
        }

        return FormatDeletedPostMarkdown(existing, publicUrl);
    }

    private async Task<BlogPost> RequireOwnedPostAsync(string slug, AuthUser user)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException("Slogs 글 slug가 필요합니다.");
        }

        var post = await blogService.GetBySlugAsync(slug, user.UserName);
        if (post is null || !post.IsAuthor(user.UserName))
        {
            throw new InvalidOperationException("수정할 수 있는 Slogs 글을 찾지 못했습니다.");
        }

        return post;
    }

    private async Task<BlogPost> UpdateOwnedPostAsync(
        BlogPost existing,
        AuthUser user,
        string? title,
        string? markdown,
        string? summary,
        string? tags,
        string? series,
        string? thumbnailUrl,
        bool? isDraft,
        string? customSlug)
    {
        var updated = await blogService.UpdatePostAsync(
            existing.Slug,
            user.UserName,
            string.IsNullOrWhiteSpace(title) ? existing.Title : title,
            summary ?? existing.Summary,
            string.IsNullOrWhiteSpace(markdown) ? existing.Body : markdown,
            tags ?? string.Join(", ", existing.Tags),
            series ?? string.Join(", ", existing.Series),
            thumbnailUrl ?? existing.ThumbnailUrl,
            isDraft,
            newSlug: existing.IsDraft ? customSlug : null);

        return updated ?? throw new InvalidOperationException("Slogs 글 업데이트에 실패했습니다.");
    }

    private AuthUser RequireUser()
        => SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User)
            ?? throw new InvalidOperationException("Slogs MCP 인증이 필요합니다. Slogs 설정에서 MCP 토큰을 만든 뒤 Authorization: Bearer 토큰으로 연결하세요.");

    private static (byte[] Bytes, string? ContentType) DecodeImage(string base64Image, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(base64Image))
        {
            throw new InvalidOperationException("업로드할 이미지 base64 데이터가 필요합니다.");
        }

        var normalizedContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();
        var payload = base64Image.Trim();
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = payload.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex <= 5)
            {
                throw new InvalidOperationException("이미지 data URL 형식이 올바르지 않습니다.");
            }

            var metadata = payload[5..commaIndex];
            if (!metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("이미지 data URL은 base64 형식이어야 합니다.");
            }

            normalizedContentType = metadata
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            payload = payload[(commaIndex + 1)..];
        }

        try
        {
            return (Convert.FromBase64String(payload), normalizedContentType);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("이미지 base64 데이터가 올바르지 않습니다.", ex);
        }
    }

    private string BuildPublicPostUrl(BlogPost post)
        => BuildAbsoluteUrl($"/@{Uri.EscapeDataString(post.Author)}/{Uri.EscapeDataString(post.Slug)}");

    private string BuildEditUrl(BlogPost post)
        => BuildAbsoluteUrl($"/edit/{Uri.EscapeDataString(post.Slug)}");

    private string BuildAbsoluteUrl(string path)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return path;
        }

        return $"{request.Scheme}://{request.Host}{path}";
    }

    private static string FormatDraftSavedPostMarkdown(BlogPost post, string editUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Slogs Post Saved Before Publishing");
        builder.AppendLine();
        AppendPostMetadata(builder, post, BuildPublicPath(post), editUrl);
        builder.AppendLine();
        builder.AppendLine("The post is saved before publishing, visible only to the owner, and not publicly listed.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUpdatedPostMarkdown(BlogPost post, string publicUrl, string editUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine(post.IsDraft ? "# Slogs Post Updated Before Publishing" : "# Slogs Post Revision Published");
        builder.AppendLine();
        AppendPostMetadata(builder, post, publicUrl, editUrl);
        return builder.ToString().TrimEnd();
    }

    private static string FormatPublishedPostMarkdown(BlogPost post, string postUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Slogs Post Published");
        builder.AppendLine();
        AppendPostMetadata(builder, post, postUrl, string.Empty);
        builder.AppendLine();
        builder.AppendLine("The post is a public Slogs site post, not an LLM Wiki entry.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatPostMarkdown(BlogPost post, string publicUrl, string editUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {post.Title}");
        builder.AppendLine();
        AppendPostMetadata(builder, post, publicUrl, editUrl);
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine(post.Summary);
        builder.AppendLine();
        builder.AppendLine("## Markdown");
        builder.AppendLine();
        builder.AppendLine(post.Body);
        return builder.ToString().TrimEnd();
    }

    private static string FormatDeletedPostMarkdown(BlogPost post, string publicUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Slogs Post Deleted");
        builder.AppendLine();
        builder.AppendLine($"- Deleted slug: `{post.Slug}`");
        builder.AppendLine($"- Former status: {(post.IsDraft ? "Pre-publish" : "Published")}");
        builder.AppendLine($"- Former URL: {publicUrl}");
        builder.AppendLine($"- Author: `@{post.Author}`");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUploadedImageMarkdown(EditorImageResponse image)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Slogs Image Uploaded");
        builder.AppendLine();
        builder.AppendLine($"- URL: {image.Url}");
        builder.AppendLine($"- Alt text: {image.AltText}");
        builder.AppendLine();
        builder.AppendLine("## Markdown");
        builder.AppendLine();
        builder.AppendLine($"![{image.AltText}]({image.Url})");
        builder.AppendLine();
        builder.AppendLine("Use this `/uploads` URL in the `markdown` body or `thumbnailUrl` of a Slogs post MCP call.");
        return builder.ToString().TrimEnd();
    }

    private static void AppendPostMetadata(StringBuilder builder, BlogPost post, string publicUrl, string editUrl)
    {
        builder.AppendLine($"- Status: {(post.IsDraft ? "Pre-publish" : "Published")}");
        if (post.IsDraft)
        {
            builder.AppendLine($"- Edit URL: {editUrl}");
        }
        else
        {
            builder.AppendLine($"- URL: {publicUrl}");
            builder.AppendLine($"- Published: {post.PublishedAt.ToUniversalTime():O}");
        }

        builder.AppendLine($"- Slug: `{post.Slug}`");
        builder.AppendLine($"- Author: `@{post.Author}`");
        builder.AppendLine($"- Updated: {post.UpdatedAt.ToUniversalTime():O}");
        builder.AppendLine($"- Read time: {post.ReadTimeMinutes} min");

        if (post.Tags.Count > 0)
        {
            builder.AppendLine($"- Tags: {string.Join(", ", post.Tags.Select(x => $"`{x}`"))}");
        }

        if (post.Series.Count > 0)
        {
            builder.AppendLine($"- Series: {string.Join(", ", post.Series.Select(x => $"`{x}`"))}");
        }

        if (!string.IsNullOrWhiteSpace(post.ThumbnailUrl))
        {
            builder.AppendLine($"- Representative image: {post.ThumbnailUrl}");
        }
    }

    private static string BuildPublicPath(BlogPost post)
        => $"/@{Uri.EscapeDataString(post.Author)}/{Uri.EscapeDataString(post.Slug)}";
}
