# Discover Jesus Backend

A .NET 8 minimal API for serving and managing dynamic page definitions, reusable
components, data bindings, and action definitions. SQL Server is used for
persistence; EF Core migrations create and evolve the database schema.

## Configure and run

Set `ConnectionStrings__DefaultConnection` to a SQL Server connection string, then
apply migrations and run the API:

```sh
export ConnectionStrings__DefaultConnection='Server=localhost;Database=DiscoverJesus;User Id=sa;Password=<your-password>;TrustServerCertificate=True'
dotnet tool restore
dotnet ef database update
dotnet run
```

Swagger UI is available at `/swagger`; the live OpenAPI document is at
`/swagger/v1/swagger.json`, generated from the registered API endpoints. The
checked-in snapshot is [api/openapi.json](api/openapi.json).

Successful response DTOs explicitly list every serialized property as required
in OpenAPI, including `false`, zero, empty collections, and opaque JSON values.
[ResponseSchemaFilter](Contracts/ResponseSchemaFilter.cs) is limited to the
named output DTOs; it does not change request validation, error responses, or
JSON serialization settings. Contract tests check these required fields against
the application's HTTP JSON serializer. `/health` documents its existing
`{ "status": "healthy" }` response through a named DTO.

## Data model

- **Pages** have unique URL slugs, titles, and a publication flag.
- **Reusable components** have globally unique friendly names and stable public
  UUIDs, locale-specific drafts, and immutable, numbered published revisions.
  Management screens use friendly names; page references continue storing UUIDs.
  Migration lineage records link source and migrated revisions by transformation
  version and batch. They are internal metadata and are removed with the
  component's revision history on explicit component deletion.
- **Page content** has locale-specific drafts and immutable published revisions.
  Its `sections` can contain inline nodes or references to reusable components.
  References resolve to the latest published revision for the requested locale,
  without locale fallback, and are expanded in delivery responses with
  `sourceComponentId` provenance.
- **Components** belong to a page and have a unique page-scoped key, type, JSON
  properties, and display order.
- **Bindings** belong to a component and describe a named data source and JSON
  configuration.
- **Actions** belong to a component and describe an action type and JSON
  configuration. The API stores and serves action definitions; it does not execute
  them.

Deleting a page or component cascades to its child records. Slugs must contain
lowercase letters and digits separated by single hyphens. Component keys, binding
names, and action names are unique within their parent.

The legacy page/component CRUD API remains available and continues to return
page-owned inline components. The new page-content routes are additive; existing
inline component records are not automatically copied into `sections`.

## API

All resource endpoints are under `/api`.

| Method | Route | Description |
| --- | --- | --- |
| GET | `/pages` | List page summaries |
| GET | `/pages/by-public-id/{pageId}` | Get the same management page by public UUID, including unpublished pages |
| POST, PUT, DELETE | `/pages`, `/pages/{id}` | Create, update, or delete a page |
| GET, POST | `/pages/{pageId}/components` | List or add page components |
| GET, PUT, DELETE | `/components/{id}` | Get, update, or delete a component |
| GET, POST | `/components/{componentId}/bindings` | List or add component bindings |
| GET, PUT, DELETE | `/bindings/{id}` | Get, update, or delete a binding |
| GET, POST | `/components/{componentId}/actions` | List or add component actions |
| GET, PUT, DELETE | `/actions/{id}` | Get, update, or delete an action definition |
| GET | `/reusable-components` | List reusable components, including withdrawn entries, by friendly name |
| POST | `/reusable-components` | Create a named reusable component and initial locale draft |
| PUT | `/reusable-components/{componentId}` | Rename a reusable component |
| GET | `/reusable-components/{componentId}?locale={locale}` | Get latest published component revision |
| GET, PUT | `/reusable-components/{componentId}/draft?locale={locale}` | Read or replace a locale draft |
| POST | `/reusable-components/{componentId}/publish?locale={locale}` | Validate and publish a component revision |
| POST, DELETE | `/reusable-components/{componentId}/withdraw`, `/reusable-components/{componentId}` | Withdraw or delete an unreferenced component |
| GET | `/content/pages/{pageId}?locale={locale}` | Deliver a published page with component references expanded |
| GET, PUT | `/content/pages/{pageId}/draft?locale={locale}` | Read or replace a locale page-content draft |
| POST | `/content/pages/{pageId}/draft/from-published?locale={locale}` | Start a draft by copying the latest published sections |
| POST | `/content/pages/{pageId}/publish?locale={locale}` | Validate and publish a page-content revision |
| GET | `/health` | API availability check |

Create and update endpoints accept JSON. `props` and `config` are arbitrary JSON
values and are returned as JSON values, not escaped JSON strings. Missing resources
return `404`, duplicate slugs or parent-scoped names return `409`, and invalid
fields return `400`.

The UUID management lookup returns `PageResponse`, including inline components,
bindings, and actions. It is not localized published-content delivery, and it
does not require the page to be published. Page creation points its `Location`
header to this stable UUID lookup; mutation routes still use integer IDs, and
slugs remain editable page fields. The slug-based detail lookup has been
removed; page summaries continue to expose slugs as editable/display fields.

Reusable-component names are trimmed, limited to 200 characters, and unique
across the entire library without regard to case. Withdrawn components retain
their names and remain visible in the management list.

The new content API uses the page's public UUID as `pageId`; legacy management
routes continue using integer IDs and slugs. Component references and typed
navigation destinations are validated when drafts are saved and published.
Page delivery rechecks component and page destinations and reports an error if
a destination is no longer available. Publishing and withdrawal authorization
has not yet been defined or implemented.

Starting a draft from published content copies the stored section JSON as-is,
preserving reusable-component references and overrides. It returns `409` if a
draft already exists and `404` if no published revision exists for the locale.

The `20261031144756_STORY004ReusableContent` migration adds UUIDs to existing
pages and creates the localized draft/revision tables. The
`20261031174222_AddReusableComponentFriendlyNames` migration backfills friendly
names from component `exampleName` metadata when present and assigns stable
fallback names otherwise. Existing inline components and opaque legacy action
configurations are not transformed by those schema migrations. See
[ReusableContent.http](ReusableContent.http) for example requests and
[the service tests](Tests/ReusableContentServiceTests.cs) for covered lifecycle,
naming, and validation behavior.

## EPIC-003 payload migration

The dry-run mode is read-only. It loads source pages, components, and reusable
component revisions, then evaluates the approved transformation mappings in
memory:

```sh
dotnet run --project Tools/Epic003DryRun/Epic003DryRun.csproj -- --dry-run
```

Set `ConnectionStrings__DefaultConnection` to the hosted development database
before running it. This mode only executes no-tracking reads; it does not apply
EF migrations, write content or lineage rows, or print transformed payloads.
It reports blocked revisions and succeeds only when the current snapshot
matches the inventoried counts for 55 source revisions, 24 navigation targets,
12 list items, and the two NavBars with four children. NavBar children use the
latest NBButton revision per definition and locale, ordered by the original
`Components.SortOrder`; `source.componentId` is used only to recover that order
during migration. If a database snapshot is obtained through another
read-only SQL client, the same transformation can be checked without a
connection string using `--dry-run-json <snapshot-file>`; the file contains
`pages`, `components`, and `revisions` arrays with the corresponding database
columns.

The separate content-writing mode is restricted to the inventoried hosted
development database and requires an explicit invocation with a caller-provided
batch UUID:

```sh
dotnet run --project Tools/Epic003DryRun/Epic003DryRun.csproj -- \
  --apply-preproduction --batch-id <new-guid>
```

Run this only after explicit owner authorization and after the
`AddReusableComponentRevisionLineage` EF migration has been applied separately.
The tool never applies schema migrations. Before writing, it checks the exact
server and database, requires the full approved inventory counts, and reruns
the mapping preflight. It creates linked immutable revisions and lineage rows
inside one serializable transaction, preserves source rows, and uses the
migration time for new `PublishedAt` values. Retries reuse complete existing
lineage; changed inventories, mapping mismatches, and partial lineage fail
before writes. The output contains counts and identifiers, not content.

## Continuous integration

The [CI workflow](.github/workflows/ci.yml) runs on pull requests targeting
`dev` or `main` and on pushes to those branches. It builds the API and tests,
runs the tests with a SQL Server container available through
`ConnectionStrings__DefaultConnection`, fails if the EF Core model has changes
without a migration, and scans NuGet packages (including transitive ones) for
known vulnerabilities. Vulnerabilities of High or Critical severity fail the
build; lower severities are reported only. It also collects line coverage,
prints the percentage in the workflow summary, and uploads the Cobertura report
as an artifact. No coverage threshold is enforced until the platform owner
approves one. The workflow generates a throwaway database password at run time
and needs no repository secrets.

Run the same checks locally:

```sh
dotnet test DiscoverJesus.sln --collect:"XPlat Code Coverage"
dotnet tool restore
dotnet dotnet-ef migrations has-pending-model-changes --project DiscoverJesus.Api.csproj
dotnet list DiscoverJesus.Api.csproj package --vulnerable --include-transitive
dotnet list Tests/DiscoverJesus.Api.Tests.csproj package --vulnerable --include-transitive
```

The integration tests use a fresh, disposable database per test on a SQL Server
instance. To run them locally with Docker, generate a throwaway password in the
shell, start a local SQL Server container, and set its connection string:

```sh
export MSSQL_SA_PASSWORD="Aa1_$(openssl rand -hex 16)"
docker run --name discover-jesus-test-sqlserver -d -p 1433:1433 \
  -e ACCEPT_EULA=Y --env MSSQL_SA_PASSWORD \
  mcr.microsoft.com/mssql/server:2022-latest
for attempt in $(seq 1 30); do
  if docker exec discover-jesus-test-sqlserver bash -c 'exec 3<>/dev/tcp/127.0.0.1/1433' 2>/dev/null; then
    break
  fi
  sleep 2
done
export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=DiscoverJesusCi;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True"
dotnet test DiscoverJesus.sln
```

The test host ignores the configured catalog and creates uniquely named
`DiscoverJesusTest_...` databases, then drops each database when its test
finishes. It refuses to run unless the configured catalog is exactly
`DiscoverJesusCi`, to avoid modifying a developer or production database. Stop
and remove the local container when finished:

```sh
docker rm -f discover-jesus-test-sqlserver
unset MSSQL_SA_PASSWORD ConnectionStrings__DefaultConnection
```

`dotnet dotnet-ef migrations has-pending-model-changes` needs
`ConnectionStrings__DefaultConnection` set to any well-formed value; it does
not connect to the database. The existing service unit tests continue using EF
Core InMemory; endpoint integration tests run through `WebApplicationFactory`
against SQL Server.

## OpenAPI contract verification

The backend's [OpenAPI document](api/openapi.json) describes this API. It is
separate from the shared repository's OpenAPI document and dynamic-content
schemas; those documents must not be substituted for it.

`OpenApiContractTests` requests `/swagger/v1/swagger.json` from an in-process
test host and compares the entire JSON document with the checked-in contract.
Whitespace and object-property order do not affect the comparison; array order
and document values do. The existing CI test command runs this check and fails
on drift. This checks generated API metadata, not conformance of every endpoint
response or JSON content payload.

Run the check without a database or running backend:

```sh
dotnet test Tests/DiscoverJesus.Api.Tests.csproj --configuration Release --filter FullyQualifiedName~OpenApiContractTests
```

The test host supplies a non-secret configuration value for startup and does
not open a database connection. It writes the served document to
`Tests/bin/Release/net8.0/TestResults/openapi.generated.json`, including when the
comparison fails. CI uploads this as the `generated-openapi` artifact.

For an intentional API change, inspect that generated document and review the
contract difference before updating:

```sh
cp Tests/bin/Release/net8.0/TestResults/openapi.generated.json api/openapi.json
git diff -- api/openapi.json
dotnet test Tests/DiscoverJesus.Api.Tests.csproj --configuration Release --filter FullyQualifiedName~OpenApiContractTests
```

Commit intentional API and contract changes together. Do not refresh the
contract merely to bypass an unexplained mismatch.
