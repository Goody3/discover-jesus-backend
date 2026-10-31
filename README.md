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

## Data model

- **Pages** have unique URL slugs, titles, and a publication flag.
- **Reusable components** have globally unique friendly names and stable public
  UUIDs, locale-specific drafts, and immutable, numbered published revisions.
  Management screens use friendly names; page references continue storing UUIDs.
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
| GET | `/pages/{slug}` | Get a page with components, bindings, and actions |
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