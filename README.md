# OpenApiContractValidation

ASP.NET Core middleware that validates—at runtime—that every HTTP **request** and **response**
fully conforms to a provided **OpenAPI** contract. On **any** contract violation it **throws**, so
drift between your API implementation and its published OpenAPI specification is caught immediately
(in development, integration tests, and CI).

It validates **everything** the contract specifies:

- **Path existence** — an undocumented path is a violation.
- **HTTP method** — a method not documented for a matched path is a violation.
- **Parameters** — path, query, header, and cookie parameters: presence (`required`), type,
  `format`, `enum`, and OpenAPI serialization **styles** (`simple`, `form`, `spaceDelimited`,
  `pipeDelimited`, `deepObject`, `matrix`, `label`, and content-based parameters).
- **Request body** — content-type matching plus full JSON Schema validation, including
  rejection of `readOnly` properties sent by the client.
- **Response body** — content-type matching plus full JSON Schema validation, including
  rejection of `writeOnly` properties leaked to the client.
- **Response headers** — declared headers are presence- and schema-checked.
- **HTTP status codes** — exact (`200`) > range (`2XX`) > `default` precedence; an
  undocumented status is a violation.

It is **maximally strict**: it enforces the contract *exactly as written* (for example, `enum`,
`required`, and `additionalProperties: false` are honored precisely)—never more strictly than the
spec, never less.

## Why validate against a hand-written spec (not one generated from code)?

This library is built for a **spec-first** workflow: author the OpenAPI document by hand, then
validate the implementation against it.

For the parts the framework can **derive from your types** (request body shape, parameter
names/types), code and spec can't disagree—but that circularity isn't harmless. There's no separate
contract artifact to review, so a model edit (rename a field, change a type, make something nullable)
**silently changes the published API**: the diff looks like an ordinary code change, not a breaking
contract change, and slips through review. With a hand-written spec, that same change must edit the
spec file, surfacing as an explicit contract change.

It gets worse for everything that depends on **annotations**, which are *not enforced* and silently
drift from the implementation:

- **Status codes** the handler returns but never declared (or declared but never returned).
- **Error bodies** documented as `ProblemDetails`/`Error` but actually a bare string or different shape.
- **Content types, headers, nullability, `readOnly`/`writeOnly`, enums, formats**—hints that quietly diverge from what the serializer really emits.

A hand-written spec is an **independent contract** instead of a mirror of the code, which is what
makes runtime validation meaningful:

- **Design the contract before implementing it**, so the API's shape is decided deliberately.
- **Two independent artifacts**, so any mismatch is a real signal—either the code regressed or the spec needs an intentional, reviewed change. Breaking changes show up in code review instead of being silently regenerated.
- **Great for AI-assisted development:** ask the AI to write the spec first → review it (small and declarative, easy to scrutinize) → have it implement → let this middleware enforce conformance, giving the agent precise, machine-readable violations to fix against.

In short: a generated spec asks *"does my spec match my code?"*; a hand-written spec validated at
runtime asks *"does my code match the contract I promised?"* If you only need documentation, generate
it. If you need a contract that can **fail the build** when the implementation drifts, write it by
hand and enforce it here.

## Features

- **Requests and responses** validated against the same contract.
- **OpenAPI 3.0.x and 3.1.x**, in **JSON or YAML**, loaded from a file, stream, or string.
- **`$ref` and recursive schemas** resolved correctly via a JSON Schema 2020-12 bridge
  (`Microsoft.OpenApi` → `JsonSchema.Net`).
- **Response bodies are buffered and validated *before* they reach the client** — when a response
  violates the contract, the offending body is never sent; the middleware throws so your exception
  handler can return a clean error.
- **Spec-authoritative path matching** — paths are matched against the OpenAPI templates
  (`/users/{id}`), not ASP.NET routing, so the contract is the single source of truth. Literal
  segments win over templated ones (`/users/me` beats `/users/{id}`).
- **Native AOT / trimming friendly** core, built on `System.Text.Json`.

## Requirements

- **.NET 10** (`net10.0`). Targets ASP.NET Core 10.

## Installation

```sh
dotnet add package OpenApiContractValidation
```

Dependencies (resolved automatically): `Microsoft.OpenApi` 2.9.0,
`Microsoft.OpenApi.YamlReader` 2.9.0, `JsonSchema.Net` 9.2.2.

## Usage

```csharp
using OpenApiContractValidation.Middleware;
using OpenApiContractValidation.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApiValidation(options =>
{
    // Source the contract from a file, a stream, or inline text:
    options.ContractFilePath = Path.Combine(AppContext.BaseDirectory, "openapi.yaml");
    // options.ContractStream = ...;
    // options.ContractText   = "...";        // with optional options.ContractFormat = "json" | "yaml"

    options.Validate = ValidationDirection.Both;   // Request | Response | Both (default: Both)
});

var app = builder.Build();

// Recommended: an exception handler ABOVE the validation middleware turns a thrown
// OpenApiContractValidationException into a clean response (e.g. 500 / ProblemDetails).
app.UseExceptionHandler("/error");

app.UseOpenApiValidation();

app.MapGet("/users/{id:int}", (int id) => Results.Json(new { id, name = "Alice" }));

app.Run();
```

### Pipeline placement

`UseOpenApiValidation()` resolves the path and method from the **OpenAPI contract** (it does not
rely on endpoint routing), so place it early enough that it wraps your endpoint dispatch. Put your
**exception-handling middleware above it**: because invalid responses are buffered and never
flushed, a thrown `OpenApiContractValidationException` propagates with `Response.HasStarted == false`,
letting your handler render a clean error.

## Handling violations

Every violation throws `OpenApiContractValidation.Errors.OpenApiContractValidationException`, which
carries:

- `Phase` — `ContractPhase.Startup`, `Request`, or `Response`.
- `HttpMethod` and `Path`.
- `Violations` — a list of `ContractViolation` records, each with a `Location`
  (e.g. `query/status`, `requestBody`, `responseBody/contentType`, `status`), an `InstanceLocation`
  (JSON Pointer into the offending body, e.g. `/id`), the failing `Keyword`, and a `Message`.

```csharp
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error
             as OpenApiContractValidationException;
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "application/problem+json";
    await ctx.Response.WriteAsJsonAsync(new
    {
        title = "OpenAPI contract violation",
        phase = ex?.Phase.ToString(),
        violations = ex?.Violations.Select(v => new { v.Location, v.InstanceLocation, v.Message }),
    });
}));
```

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `ContractFilePath` | `null` | Path to the OpenAPI document (JSON or YAML). |
| `ContractStream` | `null` | Stream providing the OpenAPI document. |
| `ContractText` | `null` | Inline OpenAPI document text. |
| `ContractFormat` | `null` | Optional `"json"` / `"yaml"` hint. |
| `Validate` | `Both` | `Request`, `Response`, or `Both`. |
| `MaxResponseBufferSizeBytes` | `10 MiB` | Cap on the buffered response body; exceeding it throws. |

Exactly one contract source (`ContractFilePath`, `ContractStream`, or `ContractText`) must be set.

## Behavior and limitations

- **Always throws** on a violation — there is no log-only mode by design.
- **Streaming** responses are not validatable: an operation declaring `text/event-stream` is
  rejected at **startup**, and a response that disables buffering at runtime throws.
- **Large responses** beyond `MaxResponseBufferSizeBytes` throw rather than silently skipping
  validation.
- `204`/`304` responses and `HEAD` requests are validated for status/headers only (no body).
- **Undocumented response headers** (e.g. `Date`, `Content-Length`) are *not* flagged—OpenAPI
  documents the headers an operation returns; it does not forbid transport headers.

## License

MIT.
