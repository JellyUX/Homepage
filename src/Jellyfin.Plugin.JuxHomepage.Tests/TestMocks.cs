using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Moq;

namespace Jellyfin.Plugin.JuxHomepage.Tests;

/// <summary>
/// Shared mock factories extracted from setup code duplicated across widget test files (Phase 2 of
/// TODO_V2.md). Each call returns a fresh instance -- do not cache/share across tests, since some
/// callers rely on a distinct Guid Id per test.
/// </summary>
internal static class TestMocks
{
    /// <summary>Returns a new default test user (fresh Guid Id each call).</summary>
    internal static User DefaultUser() => new("test", "Default", "Default");

    /// <summary>Returns a mock IUserManager whose GetUserById always returns null (unknown user).</summary>
    internal static Mock<IUserManager> UserManagerReturningNull()
    {
        var mock = new Mock<IUserManager>();
        mock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((User?)null);
        return mock;
    }

    /// <summary>Returns a mock IUserManager whose GetUserById returns the given user for any id.</summary>
    internal static Mock<IUserManager> UserManagerReturning(User user)
    {
        var mock = new Mock<IUserManager>();
        mock.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns(user);
        return mock;
    }

    /// <summary>Returns a mock IDtoService whose GetBaseItemDtos always returns an empty list.</summary>
    internal static Mock<IDtoService> DtoServiceReturningEmpty()
    {
        var mock = new Mock<IDtoService>();
        mock.Setup(m => m.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns([]);
        return mock;
    }

    /// <summary>
    /// An ILibraryManager that counts every method call per method name, can hold each call for a
    /// short synchronous block, and records the peak number of calls in flight (PROC-02.a). Calls are
    /// forwarded to <see cref="Inner"/>, which tests set up as usual to supply library data; property
    /// and event accessors are forwarded without being counted.
    /// </summary>
    internal sealed class CountingLibraryManager
    {
        private readonly ConcurrentDictionary<string, int> _counts = new();
        private int _inFlight;
        private int _maxInFlight;

        internal CountingLibraryManager()
        {
            var proxy = DispatchProxy.Create<ILibraryManager, CountingProxy>();
            ((CountingProxy)(object)proxy).Owner = this;
            Object = proxy;
        }

        /// <summary>
        /// The mock the calls are forwarded to. Unset methods return what an empty library returns: an
        /// empty array or list, a new <c>QueryResult</c>, and null for an abstract type such as BaseItem.
        /// </summary>
        internal Mock<ILibraryManager> Inner { get; } = new() { DefaultValueProvider = new EmptyLibraryValues() };

        /// <summary>The counting instance to hand to the code under test.</summary>
        internal ILibraryManager Object { get; }

        /// <summary>How long each counted call blocks its thread before being forwarded.</summary>
        internal TimeSpan BlockPerCall { get; set; } = TimeSpan.Zero;

        /// <summary>Peak number of counted calls running at the same time.</summary>
        internal int MaxInFlight => Volatile.Read(ref _maxInFlight);

        /// <summary>Total number of counted calls.</summary>
        internal int Total => _counts.Values.Sum();

        /// <summary>Number of calls to the method with the given name, all overloads together.</summary>
        internal int Count(string methodName) => _counts.GetValueOrDefault(methodName);

        /// <summary>Calls per method name, sorted by name.</summary>
        internal IReadOnlyDictionary<string, int> Counts =>
            new SortedDictionary<string, int>(_counts, StringComparer.Ordinal);

        /// <summary>Clears the counters and the peak, keeping the setups of <see cref="Inner"/>.</summary>
        internal void Reset()
        {
            _counts.Clear();
            Volatile.Write(ref _maxInFlight, 0);
        }

        private object? Forward(MethodInfo method, object?[]? args)
        {
            if (method.IsSpecialName)
            {
                return Invoke(method, args);
            }

            _counts.AddOrUpdate(method.Name, 1, (_, n) => n + 1);
            var inFlight = Interlocked.Increment(ref _inFlight);
            int peak;
            while (inFlight > (peak = Volatile.Read(ref _maxInFlight))
                && Interlocked.CompareExchange(ref _maxInFlight, inFlight, peak) != peak)
            {
            }

            try
            {
                if (BlockPerCall > TimeSpan.Zero)
                {
                    Thread.Sleep(BlockPerCall);
                }

                return Invoke(method, args);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private object? Invoke(MethodInfo method, object?[]? args)
        {
            try
            {
                return method.Invoke(Inner.Object, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private sealed class EmptyLibraryValues : DefaultValueProvider
        {
            protected override object GetDefaultValue(Type type, Mock mock)
            {
                if (type.IsValueType)
                {
                    return Activator.CreateInstance(type)!;
                }

                if (type.IsArray)
                {
                    return Array.CreateInstance(type.GetElementType()!, 0);
                }

                if (type.IsInterface && type.IsGenericType
                    && type.GetGenericArguments() is [var element]
                    && type.IsAssignableFrom(element.MakeArrayType()))
                {
                    return Array.CreateInstance(element, 0);
                }

                return !type.IsAbstract && type != typeof(string) && type.GetConstructor(Type.EmptyTypes) is not null
                    ? Activator.CreateInstance(type)!
                    : null!;
            }
        }

        /// <summary>Proxy class required by DispatchProxy; not used directly.</summary>
        internal class CountingProxy : DispatchProxy
        {
            internal CountingLibraryManager Owner { get; set; } = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
                Owner.Forward(targetMethod!, args);
        }
    }
}
