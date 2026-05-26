# NetIndex.AspNetCore

ASP.NET Core middleware for NetIndex tenant isolation. Reads `X-NetIndex-Tenant` and `X-NetIndex-Claim-*` headers from incoming requests and resolves tenant context for downstream pipeline operations.

```bash
dotnet add package NetIndex.AspNetCore
```

```csharp
builder.Services.AddNetIndex(b => b.UseAspNetCoreTenant().Build());
app.UseNetIndexTenant();
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
