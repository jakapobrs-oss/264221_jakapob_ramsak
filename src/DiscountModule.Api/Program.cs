using DiscountModule.Api;

// A thin HTTP front end: the same JSON order as the CLI goes in, the same JSON result comes out.
// Everything that decides a price lives in DiscountModule; everything that reads JSON lives in DiscountModule.Json.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Problems answer as RFC 9457 problem details (application/problem+json), including 404, 405 and crashes.
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The page at /: a small form in wwwroot that builds the same JSON the endpoint accepts and shows what it
// answers. It holds no pricing logic of its own. A path that is not a file falls through to the 404 above.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/discounts", DiscountEndpoint.PriceAsync);

app.Run();

/// <summary>Visible to the integration tests, which start the app in memory with WebApplicationFactory.</summary>
public partial class Program
{
}
