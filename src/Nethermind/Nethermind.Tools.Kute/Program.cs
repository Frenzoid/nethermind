﻿using CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Nethermind.Tools.Kute;

static class Program
{
    public static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<Config>(args).WithParsedAsync(async config =>
        {
            IServiceProvider serviceProvider = BuildServiceProvider(config);
            Application app = serviceProvider.GetService<Application>()!;

            await app.Run();
        });
    }

    static IServiceProvider BuildServiceProvider(Config config)
    {
        IServiceCollection collection = new ServiceCollection();

        collection.AddSingleton(config);
        collection.AddSingleton<Application>();
        collection.AddSingleton<ISystemClock, SystemClock>();
        collection.AddSingleton<IAuth, JwtAuth>();
        collection.AddSingleton<IJsonRpcMessageProvider, FileJsonRpcMessageProvider>();
        if (config.DryRun)
        {
            collection.AddSingleton<IJsonRpcSubmitter, NullJsonRpcSubmitter>();
        }
        else
        {
            collection.AddSingleton<IJsonRpcSubmitter, HttpJsonRpcSubmitter>();
        }

        switch (config.MetricConsumerStrategy)
        {
            case MetricConsumerStrategy.Report:
                collection.AddSingleton<IMetricsConsumer, ConsoleReportMetricsConsumer>();
                break;
            case MetricConsumerStrategy.Json:
                collection.AddSingleton<IMetricsConsumer, JsonMetricsConsumer>();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return collection.BuildServiceProvider();
    }
}
