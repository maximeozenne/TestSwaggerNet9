using Asp.Versioning;
using Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Configuration["ServiceName"] ?? throw new NullReferenceException("ServiceName is missing in the configuration file.");
var versions = new[] { 1, 2 };

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Bearer", options =>
    {
        options.Audience = "api_scope";
        options.Authority = "https://localhost:443";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidAudiences = ["api_scope"],
            ValidIssuers = ["https://localhost:443"],
        };
    });

builder.Services.AddAuthorization();

foreach (var v in versions)
{
    string version = $"v{v}";

    builder.Services.AddOpenApi(version, options =>
    {
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info = new()
            {
                Title = $"{serviceName} | {version}",
                Version = version,
                Description = "Project to test the .Net 9 OpenApi provided by Microsoft with the popular Scalar UI library"
            };

            return Task.CompletedTask;
        });

        //Adds 500 as a response status code supported by all operations in the document.
        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            operation.Responses.Add("500", new OpenApiResponse { Description = "Internal server error" });
            return Task.CompletedTask;
        });
    });
}

builder.Services.AddEndpointsApiExplorer();

builder.Services
.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.DefaultApiVersion = new ApiVersion(1.0);
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var apiVersionSet = app.NewApiVersionSet();
foreach (var version in versions.Select(v => 0.0 + v))
{
    apiVersionSet.HasApiVersion(new ApiVersion(version));
}
var versionSet = apiVersionSet
    .ReportApiVersions()
    .Build();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle(serviceName)
        .WithTheme(ScalarTheme.Kepler)
        .WithDarkMode(true)
        .WithDarkModeToggle(true)
        .WithForceThemeMode(ThemeMode.Dark)
        .WithSidebar(true)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithPreferredScheme("Bearer")
        ;
});

var baseRoute = $"/api/v{{version:apiVersion}}";

// Say Hello

var sayHello = app
    .NewVersionedApi("Say Hello")
    .WithTags("Hello");

// 1.0
var sayHelloV1 = sayHello
    .MapGroup(baseRoute + "/hello")
    .HasApiVersion(1.0);

sayHelloV1
    .MapGet("", () => "Hello !")
    .Produces<string>(StatusCodes.Status200OK, "text/plain")
    .Produces(StatusCodes.Status401Unauthorized)
    .RequireAuthorization()
    .WithSummary("Say Hello")
    .WithDescription("This endpoint allows you to say hello");

// 2.0
var sayHelloV2 = sayHello
    .MapGroup(baseRoute + "/hello")
    .HasApiVersion(2.0);

sayHelloV2
    .MapGet("/{name}", ([Description("The name of the person to say hello to.")] string name) => $"Hello {name} !")
    .Accepts<string>("application/json")
    .Produces<string>(StatusCodes.Status200OK, "text/plain")
    .Produces(StatusCodes.Status400BadRequest)
    .WithSummary("Say Hello to someone")
    .WithDescription("This endpoint allows you to say hello to someone by providing his name");


// Test Model

var testModel = app
    .NewVersionedApi("Test Model")
    .WithTags("TestModel");

// 1.0
var testModelV1 = testModel
    .MapGroup(baseRoute + "/test-model")
    .HasApiVersion(1.0);

testModelV1
    .MapPost("", (TestModel model) => model.Title)
    .Produces<string>(StatusCodes.Status200OK, "text/plain")
    .WithSummary("Extract Title from a TestModel")
    .WithDescription("Get a TestModel and returns its Title");

app.Run();

internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (authenticationSchemes.Any(authScheme => authScheme.Name == "Bearer"))
        {
            var requirements = new Dictionary<string, OpenApiSecurityScheme>
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer", // "bearer" refers to the header name here
                    BearerFormat = "Json Web Token",
                    In = ParameterLocation.Header
                }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = requirements;

            // Apply it as a requirement for all operations
            foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations))
            {
                operation.Value.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme } }] = Array.Empty<string>()
                });
            }

            document.SecurityRequirements.Add(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        }
    }
}

internal class TestModel
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}