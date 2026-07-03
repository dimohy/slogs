using Slogs.Components.Helpers;
using Slogs.Data;
using Xunit;

namespace Slogs.Tests;

public sealed class PostNavigationUrlBuilderTests
{
    [Fact]
    public void BuildPostUrlAddsGlobalMenuContext()
    {
        var post = new BlogPost { Author = "dimohy", Slug = "hello world" };

        var url = PostNavigationUrlBuilder.BuildPostUrl(post, PostNavigationUrlBuilder.GlobalMenuContext);

        Assert.Equal("/@dimohy/hello%20world?nav=global", url);
    }

    [Fact]
    public void BuildCommentsUrlPlacesFragmentAfterMenuContext()
    {
        var post = new BlogPost { Author = "dimohy", Slug = "hello" };

        var url = PostNavigationUrlBuilder.BuildCommentsUrl(post, PostNavigationUrlBuilder.PersonalMenuContext);

        Assert.Equal("/@dimohy/hello?nav=personal#comments", url);
    }

    [Fact]
    public void BuildPostUrlAddsPersonalMenuContextToDraftEditUrl()
    {
        var post = new BlogPost { Author = "dimohy", Slug = "draft", IsDraft = true };

        var url = PostNavigationUrlBuilder.BuildPostUrl(post, PostNavigationUrlBuilder.PersonalMenuContext);

        Assert.Equal("/edit/draft?nav=personal", url);
    }
}
