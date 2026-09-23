using DiscountModule.Api;

// A thin HTTP front end: the same JSON order as the CLI goes in, the same JSON result comes out.
// Everything that decides a price lives in DiscountModule; everything that reads JSON lives in DiscountModule.Json.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Problems answer as RFC 9457 problem details (application/problem+json), including 404, 405 and crashes.
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapPost("/api/discounts", DiscountEndpoint.PriceAsync);

app.Run();

/// <summary>Visible to the integration tests, which start the app in memory with WebApplicationFactory.</summary>
public partial class Program
{
}
