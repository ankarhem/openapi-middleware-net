using OpenApiContractValidation.Middleware;
using OpenApiContractValidation.Options;

var builder = WebApplication.CreateBuilder(args);

// Load the OpenAPI 3.1 contract once at startup and register the request/response validator.
builder.Services.AddOpenApiValidation(options =>
{
    options.ContractFilePath = Path.Combine(AppContext.BaseDirectory, "contract.yaml");
    options.Validate = ValidationDirection.Both;
});

var app = builder.Build();

// The validation middleware resolves paths/methods from the OpenAPI contract (it does not depend
// on endpoint routing), so place it early: it wraps the endpoint dispatch below.
app.UseOpenApiValidation();

// GET /users/{id}: a valid User (id 1 carries a recursive `manager` $ref to another User).
app.MapGet(
    "/users/{id:int}",
    (int id) =>
    {
        if (id == 1)
        {
            // Recursive: manager is itself a valid User (exercises the $ref chain).
            return Results.Json(
                new
                {
                    id = 1,
                    name = "Alice",
                    manager = new { id = 2, name = "Bob" },
                }
            );
        }

        return Results.Json(
            new { message = "user not found", code = 404 },
            statusCode: StatusCodes.Status404NotFound
        );
    }
);

// GET /users/me: literal path that must beat /users/{id} (specificity).
app.MapGet("/users/me", () => Results.Json(new { id = 99, name = "Me" }));

// GET /users: list with rich parameter styles (form, spaceDelimited, pipeDelimited, deepObject).
app.MapGet("/users", () => Results.Json(new[] { new { id = 1, name = "Alice" } }));

// POST /users: required request body (UserCreate), 201 + required Location header + User.
app.MapPost(
    "/users",
    async (HttpContext context, UserCreate create) =>
    {
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.Location = "/users/2";
        await context.Response.WriteAsJsonAsync(
            new
            {
                id = 2,
                name = create.Name,
                email = create.Email,
            }
        );
    }
);

// GET /files/{name}: binary octet-stream response.
app.MapGet(
    "/files/{name}",
    (string name) =>
        Results.File(
            System.Text.Encoding.UTF8.GetBytes("file-content-" + name),
            contentType: "application/octet-stream"
        )
);

// DELETE /no-content: 204 with no body.
app.MapDelete("/no-content", () => Results.NoContent());

// GET /etag: 304 not modified (no body).
app.MapGet("/etag", () => Results.StatusCode(StatusCodes.Status304NotModified));

// GET /widget: structured-suffix +json wildcard content type (application/vnd.widget+json).
app.MapGet(
    "/widget",
    () =>
        Results.Json(new { sku = "w-1", price = 9.99m }, contentType: "application/vnd.widget+json")
);

app.Run();

internal sealed record UserCreate(string Name, string? Email, string? Password);

public partial class Program { }
