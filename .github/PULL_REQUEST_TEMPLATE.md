## Description
<!-- Provide a concise description of the changes introduced by this PR. -->

## Related Issues
<!-- Link related issues, e.g. Fixes #123, Closes #456 -->

## Affected Packages
Please select all packages affected by this pull request:

- [ ] `EricksonLopez.Messaging.Abstractions`
- [ ] `EricksonLopez.Messaging` (Core)
- [ ] `EricksonLopez.Messaging.Generators`
- [ ] `EricksonLopez.Messaging.Analyzers`
- [ ] `EricksonLopez.Messaging.RabbitMQ`
- [ ] `EricksonLopez.Messaging.AzureServiceBus`
- [ ] `EricksonLopez.Messaging.AwsSqs`
- [ ] `EricksonLopez.Messaging.Kafka`
- [ ] `EricksonLopez.Messaging.Events`
- [ ] `EricksonLopez.Messaging.OpenTelemetry`
- [ ] `EricksonLopez.Messaging.Testing`

## Quality Gates & Verification Checklist

- [ ] Code builds cleanly with `dotnet build EricksonLopez.Messaging.slnx --configuration Release -p:TreatWarningsAsErrors=true`
- [ ] All automated tests pass (`dotnet test EricksonLopez.Messaging.slnx --configuration Release`)
- [ ] Packaging succeeds for all 11 packages (`dotnet pack EricksonLopez.Messaging.slnx --configuration Release`)
- [ ] New/modified logic includes unit, concurrency, or integration tests
- [ ] Stryker mutation testing quality gate verified ($\ge 95\%$)
- [ ] Benchmark regression gate verified (zero allocation hot paths, $\le 5\%$ latency regression)
- [ ] Roslyn diagnostic rules (`ELMSG002`, `ELMSG004`, `ELMSG005`, `ELMSG008`, `ELMSG010`) satisfied
- [ ] Native AOT and Trimming compatibility preserved (`<IsAotCompatible>true</IsAotCompatible>`)
- [ ] Documentation updated in `README.md` or `/docs/` if public APIs were modified
- [ ] Commit history follows [Conventional Commits](https://www.conventionalcommits.org/)
