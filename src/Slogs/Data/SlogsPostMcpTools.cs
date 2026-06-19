using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Slogs.Data;

[McpServerToolType]
public sealed class SlogsPostMcpTools(IHttpContextAccessor httpContextAccessor, BlogService blogService)
{
    [McpServerTool(Name = "slogs_post_save_draft")]
    [Description("Save a Markdown Slogs post as a draft for the authenticated user. Use this for temporary saves. If slug is provided, the owned post is updated and kept as draft.")]
    public async Task<string> SaveDraftAsync(
        [Description("Optional owned post slug to update. Omit to create a new draft.")] string? slug = null,
        [Description("Draft title. Required when creating a draft unless markdown is provided.")] string? title = null,
        [Description("Markdown body. Required when creating a draft unless title is provided.")] string? markdown = null,
        [Description("Optional post summary. If omitted on create, Slogs derives a short summary from the Markdown body.")] string? summary = null,
        [Description("Optional comma-separated tags. Slogs keeps up to five tags and uses general when empty.")] string? tags = null,
        [Description("Optional series name.")] string? series = null,
        [Description("Optional representative image URL. Use http, https, or /uploads paths only. If omitted, Slogs uses the first Markdown body image when present.")] string? thumbnailUrl = null)
    {
        var user = RequireUser();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var existing = await RequireOwnedPostAsync(slug, user);
            var post = await UpdateOwnedPostAsync(existing, user, title, markdown, summary, tags, series, thumbnailUrl, isDraft: true);
            return FormatDraftSavedPostMarkdown(post, BuildEditUrl(post));
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Slogs 임시저장에는 제목이나 Markdown 본문 중 하나가 필요합니다.");
        }

        var draft = await blogService.CreatePostAsync(
            title ?? string.Empty,
            user.UserName,
            summary ?? string.Empty,
            markdown ?? string.Empty,
            tags ?? string.Empty,
            series,
            thumbnailUrl,
            isDraft: true);

        return FormatDraftSavedPostMarkdown(draft, BuildEditUrl(draft));
    }

    [McpServerTool(Name = "slogs_post_update")]
    [Description("Update an owned Markdown Slogs post without changing its draft/published status. Use this after reading the current post and deciding what fields to replace.")]
    public async Task<string> UpdateAsync(
        [Description("Owned Slogs post slug to update.")] string slug,
        [Description("Optional replacement title. Omitted fields keep their current values.")] string? title = null,
        [Description("Optional replacement Markdown body. Omitted fields keep their current values.")] string? markdown = null,
        [Description("Optional replacement summary. Omitted fields keep their current values.")] string? summary = null,
        [Description("Optional replacement comma-separated tags. Omitted fields keep their current values.")] string? tags = null,
        [Description("Optional replacement series name. Omitted fields keep their current values.")] string? series = null,
        [Description("Optional replacement representative image URL. Omitted fields keep their current values. If the resulting value is empty, Slogs uses the first Markdown body image when present.")] string? thumbnailUrl = null)
    {
        var user = RequireUser();
        var existing = await RequireOwnedPostAsync(slug, user);
        var post = await UpdateOwnedPostAsync(existing, user, title, markdown, summary, tags, series, thumbnailUrl, isDraft: null);
        return FormatUpdatedPostMarkdown(post, BuildPublicPostUrl(post), BuildEditUrl(post));
    }

    [McpServerTool(Name = "slogs_post_publish")]
    [Description("Publish a Markdown Slogs post for the authenticated user. If slug is provided, publish that owned draft/post after applying optional updates. If slug is omitted, create a new published site post. This creates normal Slogs posts, not LLM Wiki memories.")]
    public async Task<string> PublishAsync(
        [Description("Optional owned draft/post slug to publish. Omit to create and publish a new post.")] string? slug = null,
        [Description("Public post title. Required when slug is omitted. Optional replacement when slug is provided.")] string? title = null,
        [Description("Markdown body to publish as Slogs post content. Required when slug is omitted. Optional replacement when slug is provided.")] string? markdown = null,
        [Description("Optional post summary. If omitted on create, Slogs derives a short summary from the Markdown body.")] string? summary = null,
        [Description("Optional comma-separated tags. Slogs keeps up to five tags and uses general when empty.")] string? tags = null,
        [Description("Optional series name.")] string? series = null,
        [Description("Optional representative image URL. Use http, https, or /uploads paths only. If omitted, Slogs uses the first Markdown body image when present.")] string? thumbnailUrl = null)
    {
        var user = RequireUser();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var existing = await RequireOwnedPostAsync(slug, user);
            var publishedExistingPost = await UpdateOwnedPostAsync(existing, user, title, markdown, summary, tags, series, thumbnailUrl, isDraft: false);
            return FormatPublishedPostMarkdown(publishedExistingPost, BuildPublicPostUrl(publishedExistingPost));
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
            isDraft: false);

        return FormatPublishedPostMarkdown(publishedPost, BuildPublicPostUrl(publishedPost));
    }

    [McpServerTool(Name = "slogs_post_read")]
    [Description("Read an owned or public Slogs post by slug. This reads normal Slogs posts, including the authenticated user's drafts, not LLM Wiki memories.")]
    public async Task<string> ReadAsync(
        [Description("Post slug returned by Slogs post MCP tools or visible in a Slogs post URL.")] string slug)
    {
        var user = RequireUser();
        var post = await blogService.GetBySlugAsync(slug, user.UserName);
        return post is null
            ? "Slogs post not found."
            : FormatPostMarkdown(post, BuildPublicPostUrl(post), BuildEditUrl(post));
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
        bool? isDraft)
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
            isDraft);

        return updated ?? throw new InvalidOperationException("Slogs 글 업데이트에 실패했습니다.");
    }

    private AuthUser RequireUser()
        => SlogsAuthentication.TryCreateUser(httpContextAccessor.HttpContext?.User)
            ?? throw new InvalidOperationException("Slogs MCP 인증이 필요합니다. Slogs 설정에서 MCP 토큰을 만든 뒤 Authorization: Bearer 토큰으로 연결하세요.");

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
        builder.AppendLine("# Slogs Post Draft Saved");
        builder.AppendLine();
        AppendPostMetadata(builder, post, BuildPublicPath(post), editUrl);
        builder.AppendLine();
        builder.AppendLine("The post is saved as a Slogs draft and is not publicly listed.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUpdatedPostMarkdown(BlogPost post, string publicUrl, string editUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Slogs Post Updated");
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

    private static void AppendPostMetadata(StringBuilder builder, BlogPost post, string publicUrl, string editUrl)
    {
        builder.AppendLine($"- Status: {(post.IsDraft ? "Draft" : "Published")}");
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
