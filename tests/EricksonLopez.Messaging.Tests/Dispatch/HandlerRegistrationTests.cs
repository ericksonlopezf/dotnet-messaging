// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Dispatch;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Result;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public sealed class HandlerRegistrationTests
{
    private sealed record SampleMsg(string Content) : IMessage;
    private sealed class SampleHandler : IMessageHandler<SampleMsg>
    {
        public ValueTask<Result> HandleAsync(SampleMsg message, MessageContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());
    }

    private sealed class TestRegistrationBase : HandlerRegistrationBase
    {
        public bool DispatcherRegistered { get; private set; }

        public TestRegistrationBase(string typeName) : base(typeName) { }

        public override void Register(DefaultMessageDispatcher dispatcher)
        {
            DispatcherRegistered = true;
        }
    }

    private sealed class CustomHandlerRegistration : IHandlerRegistration
    {
        public string TypeName => "custom.msg";
        public bool DispatcherRegistered { get; private set; }

        public void Register(DefaultMessageDispatcher dispatcher)
        {
            DispatcherRegistered = true;
        }
    }

    [Fact]
    public void HandlerRegistration_RegisterWithIHandlerRegistry_CallsRegisterHandler()
    {
        var registration = new HandlerRegistration<SampleMsg, SampleHandler>("sample.msg");
        var registry = Substitute.For<IHandlerRegistry>();

        registration.Register(registry);

        registry.Received(1).RegisterHandler<SampleMsg, SampleHandler>("sample.msg");
    }

    [Fact]
    public void HandlerRegistration_RegisterNullRegistry_ThrowsArgumentNullException()
    {
        var registration = new HandlerRegistration<SampleMsg, SampleHandler>("sample.msg");

        var act = () => registration.Register((IHandlerRegistry)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }

    [Fact]
    public void HandlerRegistration_RegisterNullDispatcher_ThrowsArgumentNullException()
    {
        var registration = new HandlerRegistration<SampleMsg, SampleHandler>("sample.msg");

        var act = () => registration.Register((DefaultMessageDispatcher)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dispatcher");
    }

    [Fact]
    public void HandlerRegistrationBase_RegisterIHandlerRegistry_WhenDispatcher_CallsRegisterDispatcher()
    {
        var reg = new TestRegistrationBase("test.msg");
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);

        reg.Register((IHandlerRegistry)dispatcher);

        reg.DispatcherRegistered.Should().BeTrue();
    }

    [Fact]
    public void HandlerRegistrationBase_RegisterIHandlerRegistry_WhenNotDispatcher_DoesNotThrow()
    {
        var reg = new TestRegistrationBase("test.msg");
        var registry = Substitute.For<IHandlerRegistry>();

        reg.Register(registry);

        reg.DispatcherRegistered.Should().BeFalse();
    }

    [Fact]
    public void IHandlerRegistration_DefaultInterfaceMethod_WhenDispatcher_CallsRegisterDispatcher()
    {
        IHandlerRegistration reg = new CustomHandlerRegistration();
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);

        reg.Register(dispatcher);

        ((CustomHandlerRegistration)reg).DispatcherRegistered.Should().BeTrue();
    }

    [Fact]
    public void IHandlerRegistration_DefaultInterfaceMethod_WhenNotDispatcher_DoesNotThrow()
    {
        IHandlerRegistration reg = new CustomHandlerRegistration();
        var registry = Substitute.For<IHandlerRegistry>();

        reg.Register(registry);

        ((CustomHandlerRegistration)reg).DispatcherRegistered.Should().BeFalse();
    }
}
