using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Services
{
    /// <summary>
    /// Simple service locator for dependency injection
    /// </summary>
    public static class ServiceLocator
    {
        private static IServiceProvider? _serviceProvider;

        public static void Initialize()
        {
            var services = new ServiceCollection();

            // Register services
            services.AddSingleton<ILogger, Logger>();
            services.AddSingleton<IHashService, HashService>();
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<IScanningService, ScanningService>();

            _serviceProvider = services.BuildServiceProvider();
        }

        public static T GetService<T>() where T : notnull
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("Services not initialized. Call Initialize() first.");

            return _serviceProvider.GetRequiredService<T>();
        }

        public static object GetService(Type serviceType)
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("Services not initialized. Call Initialize() first.");

            return _serviceProvider.GetRequiredService(serviceType);
        }
    }

    // Simple service collection implementation
    internal class ServiceCollection
    {
        private readonly Dictionary<Type, object> _services = new();
        private readonly Dictionary<Type, Type> _serviceTypes = new();

        public void AddSingleton<TInterface, TImplementation>() where TImplementation : TInterface
        {
            _serviceTypes[typeof(TInterface)] = typeof(TImplementation);
        }

        public IServiceProvider BuildServiceProvider()
        {
            return new ServiceProvider(this);
        }

        private class ServiceProvider : IServiceProvider
        {
            private readonly ServiceCollection _services;
            private readonly Dictionary<Type, object> _instances = new();

            public ServiceProvider(ServiceCollection services)
            {
                _services = services;
            }

            public object GetRequiredService(Type serviceType)
            {
                return GetService(serviceType) ?? throw new InvalidOperationException($"Service {serviceType} not found");
            }

            public object? GetService(Type serviceType)
            {
                if (_instances.TryGetValue(serviceType, out var instance))
                    return instance;

                if (_services._serviceTypes.TryGetValue(serviceType, out var implementationType))
                {
                    // Create instance with constructor injection
                    var constructors = implementationType.GetConstructors();
                    if (constructors.Length == 0)
                        throw new InvalidOperationException($"No constructor found for {implementationType}");

                    var constructor = constructors[0];
                    var parameters = constructor.GetParameters();
                    var args = new object[parameters.Length];

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        args[i] = GetService(parameters[i].ParameterType) ??
                                 throw new InvalidOperationException($"Cannot resolve dependency {parameters[i].ParameterType}");
                    }

                    instance = Activator.CreateInstance(implementationType, args);
                    _instances[serviceType] = instance!;
                    return instance;
                }

                return null;
            }
        }
    }
}
