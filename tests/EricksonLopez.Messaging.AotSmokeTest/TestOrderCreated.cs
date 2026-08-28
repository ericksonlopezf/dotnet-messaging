// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.AotSmokeTest;

/// <summary>
/// Sample order created record used for Native AOT smoke testing.
/// </summary>
public sealed record TestOrderCreated(Guid Id, string OrderNumber, decimal Total);
