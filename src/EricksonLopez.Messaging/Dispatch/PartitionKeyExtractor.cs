// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace EricksonLopez.Messaging.Dispatch;

[UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Inspects message contracts for PartitionKey attribute.")]
[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Inspects message contracts for PartitionKey attribute.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Inspects message contracts for PartitionKey attribute.")]
internal static class PartitionKeyExtractor<TMessage>
{
    private static readonly Func<TMessage, string?>? Getter = CreateGetter();

    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Inspects message contracts for PartitionKey attribute.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Inspects message contracts for PartitionKey attribute.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Inspects message contracts for PartitionKey attribute.")]
    private static Func<TMessage, string?>? CreateGetter()
    {
        var props = typeof(TMessage).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (prop.GetCustomAttribute<EricksonLopez.Messaging.Attributes.PartitionKeyAttribute>() != null)
            {
                var getMethod = prop.GetMethod;
                if (getMethod is not null && !getMethod.IsStatic)
                {
                    try
                    {
                        var propType = prop.PropertyType;
                        if (propType == typeof(string))
                        {
                            return (Func<TMessage, string?>)Delegate.CreateDelegate(typeof(Func<TMessage, string?>), getMethod);
                        }

                        if (propType == typeof(Guid))
                        {
                            var guidGetter = (Func<TMessage, Guid>)Delegate.CreateDelegate(typeof(Func<TMessage, Guid>), getMethod);
                            return msg => guidGetter(msg).ToString("N");
                        }

                        if (propType == typeof(Guid?))
                        {
                            var nullableGuidGetter = (Func<TMessage, Guid?>)Delegate.CreateDelegate(typeof(Func<TMessage, Guid?>), getMethod);
                            return msg => nullableGuidGetter(msg)?.ToString("N");
                        }

                        if (propType == typeof(int))
                        {
                            var intGetter = (Func<TMessage, int>)Delegate.CreateDelegate(typeof(Func<TMessage, int>), getMethod);
                            return msg => intGetter(msg).ToString(CultureInfo.InvariantCulture);
                        }

                        if (propType == typeof(int?))
                        {
                            var nullableIntGetter = (Func<TMessage, int?>)Delegate.CreateDelegate(typeof(Func<TMessage, int?>), getMethod);
                            return msg => nullableIntGetter(msg)?.ToString(CultureInfo.InvariantCulture);
                        }

                        if (propType == typeof(long))
                        {
                            var longGetter = (Func<TMessage, long>)Delegate.CreateDelegate(typeof(Func<TMessage, long>), getMethod);
                            return msg => longGetter(msg).ToString(CultureInfo.InvariantCulture);
                        }

                        if (propType == typeof(long?))
                        {
                            var nullableLongGetter = (Func<TMessage, long?>)Delegate.CreateDelegate(typeof(Func<TMessage, long?>), getMethod);
                            return msg => nullableLongGetter(msg)?.ToString(CultureInfo.InvariantCulture);
                        }

                        if (propType == typeof(DateTime))
                        {
                            var dtGetter = (Func<TMessage, DateTime>)Delegate.CreateDelegate(typeof(Func<TMessage, DateTime>), getMethod);
                            return msg => dtGetter(msg).ToString("O", CultureInfo.InvariantCulture);
                        }

                        if (propType == typeof(DateTimeOffset))
                        {
                            var dtoGetter = (Func<TMessage, DateTimeOffset>)Delegate.CreateDelegate(typeof(Func<TMessage, DateTimeOffset>), getMethod);
                            return msg => dtoGetter(msg).ToString("O", CultureInfo.InvariantCulture);
                        }
                    }
                    catch
                    {
                        // fallback to reflection invocation
                    }
                }

                return msg => prop.GetValue(msg)?.ToString();
            }
        }
        return null;
    }

    public static string? Extract(TMessage message)
    {
        return Getter?.Invoke(message);
    }
}
