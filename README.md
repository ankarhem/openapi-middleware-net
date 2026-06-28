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

This library is designed for a **spec-first** workflow: you author the OpenAPI document by hand and
validate the implementation against it. That is deliberately the opposite of the common
**code-first** approach, where the spec is generated from controllers, attributes, or reflection.

Generating the spec from code makes the spec a *mirror* of the implementation. Validating against a
generated spec is therefore close to circular—**the code is checked against a description of
itself**, so it can never disagree:

- **A generated spec can't catch the bug, because the bug generates the spec.** If a handler
  returns the wrong shape, forgets a `required` field, or emits an undocumented status, the
  generator faithfully writes *that* into the spec. The "contract" silently drifts to match the
  defect, and a code-first validator sees no violation.
- **The spec stops being a contract and becomes documentation of current behavior.** A contract is
  a promise made *to consumers* that the code must honor. A generated spec is a report of whatever
  the code happens to do today—useful, but it can't hold the code accountable.

A **hand-written spec is an independent source of truth**, and that independence is exactly what
makes runtime validation meaningful:

- **It's a real, two-party check.** The spec and the implementation are produced separately, so
  this middleware compares two independent artifacts. Any disagreement is a genuine signal:
  either the code regressed or the spec needs an intentional, reviewed change.
- **Breaking changes become visible and deliberate.** Because the spec is committed and
  reviewed like any other interface, a change to it shows up in code review. With a generated
  spec, a breaking change to your API can ship invisibly—the generator just regenerates a new
  "contract" to match.
- **The contract is designed, not derived.** You decide the shape consumers depend on—status
  codes, nullability, `readOnly`/`writeOnly`, enums, examples—rather than inheriting whatever your
  serializer, model attributes, or framework defaults happen to produce.
- **It's framework- and refactor-independent.** A rename, a serializer setting, a new attribute, or
  an upgrade can quietly change a generated spec. A hand-written spec only changes when *you* change
  it, so refactors that accidentally alter the public contract are caught instead of absorbed.
- **It enables true consumer-driven and design-first development.** Front-end teams, partners, and
  mock servers can build against the agreed spec before the implementation exists; this middleware
  then proves the implementation lives up to it.

In short: **a generated spec asks "does my spec match my code?" (always yes). A hand-written spec
validated at runtime asks "does my code match the contract I promised?"—the question that actually
protects your consumers.** If you only need documentation, generate it. If you need a contract that
can *fail the build* when the implementation drifts, write it by hand and enforce it here.

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
