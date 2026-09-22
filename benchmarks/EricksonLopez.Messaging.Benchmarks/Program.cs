// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Defines the entry point for running messaging performance benchmarks.
/// </summary>
public static class Program
{
    /// <summary>
    /// Executes the benchmark test suite.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<MessagingBenchmarks>();
    }
}





