# ADR-008: Result Pattern Integration in Handlers

## Status
Rejected

## Date
2026-09-04

## Context
Exceptions are computationally expensive for expected business failures and distort control flow.

## Decision
- `IMessageHandler<TMessage>.HandleAsync` returns `ValueTask<Result>`.
- Functional failures (`Result.Failure(Error)`) represent deterministic domain/application rejections and are acknowledged (`ACK`) to prevent infinite poison message loops.
- Unhandled technical exceptions or transport errors are caught by `ExceptionHandlingMiddleware` and trigger retry/DLQ policies.

## Consequences
- Clean distinction between business validation failures and infrastructure transport faults.
- Zero exception-allocation overhead on business rejection paths.
