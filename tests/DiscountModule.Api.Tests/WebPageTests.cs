using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace DiscountModule.Api.Tests;

/// <summary>
/// The page in wwwroot: the API serves it at the root, its script and style come with it, and a path that is
/// not a file still answers with problem details like any other unknown path.
/// </summary>
public class WebPageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task The_page_is_served_at_the_root(string path)
    {
        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Discount Module</title>", html);
        Assert.Contains("app.js", html);
        Assert.Contains("app.css", html);
    }

    [Theory]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/app.css", "text/css")]
    public async Task The_script_and_the_style_of_the_page_are_served(string path, string mediaType)
    {
        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_script_posts_to_the_discount_endpoint()
    {
        string script = await client.GetStringAsync("/app.js");

        Assert.Contains("/api/discounts", script);
    }

    [Fact]
    public async Task Every_element_the_script_looks_up_by_id_exists_in_the_page()
    {
        // The script finds its controls with $("id"). A renamed or deleted element would only fail in a browser,
        // so this ties the two files together: every id the script asks for must be declared in the page.
        string html = await client.GetStringAsync("/");
        string script = await client.GetStringAsync("/app.js");
        string[] lookedUp = [.. Regex.Matches(script, """\$\("([\w-]+)"\)""").Select(match => match.Groups[1].Value).Distinct()];

        Assert.NotEmpty(lookedUp);
        Assert.All(lookedUp, id => Assert.Contains($"id=\"{id}\"", html));
    }

    [Theory]
    [InlineData("/missing.html")]
    [InlineData("/README.md")]
    [InlineData("/appsettings.json")]
    public async Task A_path_that_is_not_a_file_of_the_page_still_answers_with_problem_details(string path)
    {
        // Only wwwroot is served: a file of the repository or of the API project is not reachable this way.
        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
