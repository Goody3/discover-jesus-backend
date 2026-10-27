# Discover Jesus Backend

A .NET 8 minimal API for serving and managing dynamic page definitions, components,
data bindings, and action definitions. SQL Server is used for persistence; EF Core
migrations create and evolve the database schema.

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
`/swagger/v1/swagger.json`. The checked-in OpenAPI document is
[`api/openapi.json`](api/openapi.json).

## Data model

- **Pages** have unique URL slugs, titles, and a publication flag.
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
| GET | `/health` | API availability check |

Create and update endpoints accept JSON. `props` and `config` are arbitrary JSON
values and are returned as JSON values, not escaped JSON strings. Missing resources
return `404`, duplicate slugs or parent-scoped names return `409`, and invalid
fields return `400`.