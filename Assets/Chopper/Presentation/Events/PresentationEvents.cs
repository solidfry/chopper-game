using System;
using System.Collections.Generic;
using MessagePipe;

namespace Chopper.Presentation.Events
{
    /// <summary>
    /// Typed message channel for presentation (MessagePipe). One per <see cref="Bridge.PresentationContext"/>, so
    /// nothing is global. TODO: build the brokers in the match <c>LifetimeScope</c> (VContainer) when it exists.
    /// </summary>
    public sealed class PresentationEvents : IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly Dictionary<Type, object> _publishers = new();
        private readonly Dictionary<Type, object> _subscribers = new();

        public PresentationEvents()
        {
            var builder = new BuiltinContainerBuilder();
            builder.AddMessagePipe();
            builder.AddMessageBroker<LocalPlayerIdentified>();
            builder.AddMessageBroker<HitConfirmed>();
            builder.AddMessageBroker<DamageTaken>();
            builder.AddMessageBroker<ScoreChanged>();
            builder.AddMessageBroker<PlayerLeft>();
            builder.AddMessageBroker<PlayerKilled>();
            builder.AddMessageBroker<MatchStateChanged>();
            builder.AddMessageBroker<RocketLaunched>();
            builder.AddMessageBroker<MissileDetonated>();
            _provider = builder.BuildServiceProvider();
        }

        public void Publish<T>(T message) => Get<IPublisher<T>, T>(_publishers).Publish(message);

        /// <summary>Dispose the result to unsubscribe.</summary>
        public IDisposable Subscribe<T>(Action<T> handler) => Get<ISubscriber<T>, T>(_subscribers).Subscribe(handler);

        public void Dispose()
        {
            (_provider as IDisposable)?.Dispose();
            _publishers.Clear();
            _subscribers.Clear();
        }

        private TService Get<TService, TMessage>(Dictionary<Type, object> cache)
        {
            var type = typeof(TMessage);
            if (cache.TryGetValue(type, out var cached)) return (TService)cached;

            var service = _provider.GetService(typeof(TService)) ??
                          throw new InvalidOperationException($"Message {type.Name} is not registered in {nameof(PresentationEvents)}.");
            cache[type] = service;
            return (TService)service;
        }
    }
}
