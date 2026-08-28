# Support Guide

Welcome to the `EricksonLopez.Messaging` support hub. We are committed to maintaining high standards of software quality and developer experience.

---

## Support Channels

| Channel | Best For | Link |
| :--- | :--- | :--- |
| **GitHub Issues** | Confirmed bug reports, regression tracking, and verified feature requests | [Open an Issue](https://github.com/ericksonlopezf/dotnet-messaging/issues) |
| **GitHub Discussions** | General questions, architecture guidance, and design ideas | [Join Discussions](https://github.com/ericksonlopezf/dotnet-messaging/discussions) |
| **Security Advisories** | Confidential security vulnerability reporting | [Security Policy](SECURITY.md) / [Email](mailto:ericksonlopezf@gmail.com) |
| **Documentation** | Architectural guides, API references, CI/CD specs, and ADRs | [Documentation Index](docs/architecture.md) |

---

## How to Get Help

When seeking help or filing an issue, providing concise technical context allows maintainers to assist more efficiently:

1. **Check Existing Documentation**:
   - Review [Architecture Overview](docs/architecture.md) and [Public API Reference](docs/public-api-reference.md).
   - Check the [Ecosystem Integration Guide](docs/ecosystem-integration.md) for interactions with `dotnet-events` and `dotnet-outbox`.
   - Search closed and open [GitHub Issues](https://github.com/ericksonlopezf/dotnet-messaging/issues).
2. **Provide Detailed Reproduction Details**:
   - Package version (`EricksonLopez.Messaging` version).
   - Target runtime (`.NET 10.0`, Native AOT enabled/disabled).
   - Transport provider involved (`RabbitMQ`, `AzureServiceBus`, `AwsSqs`, `Kafka`, `InMemory`).
   - Minimal reproducing code sample or unit test.

---

## Triage SLAs & Expectations

- **Bug Reports & Issues**: Triaged within 3 to 5 business days.
- **Pull Requests**: Reviewed within 5 to 7 business days.
- **Security Inquiries**: Initial response within 48 hours.
