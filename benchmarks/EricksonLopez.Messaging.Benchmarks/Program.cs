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

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<MessagingBenchmarks>();
    }
}





