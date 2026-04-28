# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in NetIndex, please **do not** open a public GitHub issue. Instead, follow responsible disclosure:

1. **Email** — Send details to: security@netindex.dev
2. **Timeline** — We will acknowledge within 48 hours and provide an initial assessment within 5 business days
3. **Embargo** — We request a 90-day embargo before public disclosure to allow time for patching
4. **Credit** — We will credit you in the security advisory if you wish

### Information to Include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if you have one)

## Supported Versions

| Version | Status | Security Updates |
|---------|--------|------------------|
| 1.x     | Current | ✅ Yes |
| 0.9.x   | Preview | ✅ Critical only |
| < 0.9   | EOL    | ❌ No |

## Security Best Practices

### For Users

1. **Keep .NET Updated** — Use .NET 9 LTS or later
2. **Use Managed Identity** — Prefer `DefaultAzureCredential` over API keys
3. **Configure Auth** — Always configure `ITenantResolver`; deny-all is the default
4. **Encrypt Vectors** — Use TLS in transit; consider encryption at rest
5. **Audit Logs** — Enable structured logging and monitoring

### For Contributors

1. **No API Keys in Code** — Use configuration/secrets management
2. **Input Validation** — Validate all external input
3. **Dependency Security** — Run `dotnet list package --vulnerable` regularly
4. **Async Patterns** — Proper `CancellationToken` handling prevents resource exhaustion
5. **Error Handling** — Don't leak sensitive information in exception messages

## Dependencies

NetIndex uses minimal external dependencies, focusing on Microsoft.Extensions.* and proven battle-tested libraries:

- `Microsoft.Extensions.AI` — LLM/embedding abstractions
- `Microsoft.Extensions.VectorData` — Vector store abstractions
- `OllamaSharp` — Ollama integration
- `xunit` + `NSubstitute` — Testing only

All packages are regularly scanned for vulnerabilities via:
- GitHub Dependabot
- NuGet advisory feeds
- OWASP vulnerability databases

## Known Issues

None currently reported. Vulnerabilities will be disclosed here with CVE details once patched.

## Security Checklist

- [ ] No API keys committed to version control
- [ ] All async I/O uses `CancellationToken`
- [ ] Dependency graph is acyclic (enforced by NetArchTest)
- [ ] Exceptions don't leak sensitive data
- [ ] Auth defaults to deny-all (no silent success)
- [ ] Structured logging captures audit trails
- [ ] SQL injection prevented (parameterized queries)
- [ ] Secrets not logged or exposed in telemetry

---

**Security is a shared responsibility. Thank you for helping keep NetIndex safe.**
