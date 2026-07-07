using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class BlogPostIdentityTests
{
    [Theory]
    [InlineData("dimohy")]
    [InlineData("@dimohy")]
    [InlineData("/@dimohy")]
    [InlineData(" DIMOHy ")]
    public void IsAuthorAcceptsHandleForms(string userName)
    {
        var post = new BlogPost { Author = "dimohy" };

        Assert.True(post.IsAuthor(userName));
    }

    [Theory]
    [InlineData("dimohy")]
    [InlineData("@dimohy")]
    [InlineData("/@dimohy")]
    [InlineData(" DIMOHy ")]
    public void CommentIsAuthorAcceptsHandleForms(string userName)
    {
        var comment = new BlogComment { Author = "dimohy" };

        Assert.True(comment.IsAuthor(userName));
    }
}
