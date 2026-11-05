using Envoy.Core.Configuration;
using Envoy.Core.Data;
using Envoy.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Envoy.Core.Services;

public static class ServiceRegistration
{
    public static IServiceCollection AddEnvoyCore(this IServiceCollection services)
    {
        // A factory (not a scoped context) so the singleton views that hold repositories
        // never capture a long-lived DbContext. Each repository creates a short-lived
        // context per operation, so there's no shared change tracker and no concurrent-
        // access crash.
        services.AddDbContextFactory<EnvoyDbContext>();

        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<ITailoredProfileRepository, TailoredProfileRepository>();
        services.AddSingleton<IApplicationLogRepository, ApplicationLogRepository>();
        services.AddSingleton<IJobEventRepository, JobEventRepository>();
        services.AddSingleton<IOcrService, TesseractOcrService>();

        services.AddSingleton<HardwareProfiler>();
        services.AddSingleton<SafetyService>();
        // APP-16: the typing-variance / mouse-path settings knobs actually feed the service now.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<EnvoySettings>();
            return new HumanizationService(settings.TypingSpeedVariance, settings.MousePathSteps);
        });
        services.AddSingleton<CdpBrowserService>();
        services.AddSingleton<IBrowserLauncher, BrowserLauncher>();

        services.AddSingleton(EnvoySettings.Load());
        services.AddSingleton<IUpdateCheckService>(sp =>
            new UpdateCheckService(new HttpClient(), sp.GetRequiredService<ILogger<UpdateCheckService>>()));
        services.AddSingleton<LLMDetectionService>();
        services.AddSingleton<OllamaService>(sp =>
        {
            var detection = sp.GetRequiredService<LLMDetectionService>();
            var log = sp.GetRequiredService<ILogger<OllamaService>>();
            var provider = detection.CreateActiveProvider();
            return new OllamaService(provider, log);
        });

        services.AddSingleton<RelocationLogger>();
        services.AddSingleton<IBrowserQuery>(sp => new CdpBrowserQueryAdapter(sp.GetRequiredService<CdpBrowserService>()));
        services.AddSingleton<IElementLocator, ElementLocatorService>();

        services.AddSingleton<TemplateEngine>(sp =>
            new TemplateEngine(
                ResolveTemplatesDir(sp), sp.GetRequiredService<IElementLocator>()));

        static string ResolveTemplatesDir(IServiceProvider sp)
        {
            var settings = sp.GetRequiredService<EnvoySettings>();
            if (!string.IsNullOrWhiteSpace(settings.TemplatesPath)) // APP-16: knob now wired
                return settings.TemplatesPath;
            return Path.Combine(AppContext.BaseDirectory, "Templates");
        }

        services.AddSingleton<ResumeParserService>();
        services.AddSingleton<TailoringEngine>();
        services.AddSingleton<ApplicationOrchestrator>();

        // APP-09: generic field mapping — the no-template fallback for any ATS.
        services.AddSingleton<IFormFieldEnumerator>(sp => new CdpFormFieldEnumerator(
            new CdpBrowserQueryAdapter(sp.GetRequiredService<CdpBrowserService>())));
        services.AddSingleton<IFormFieldActuator, CdpFormFieldActuator>();
        services.AddSingleton<IAnswerBankRepository>(sp => new AnswerBankRepository(
            AnswerBankRepository.DefaultPath));
        services.AddSingleton<GenericFormFiller>();
        services.AddSingleton<FormFieldMapper>();

        // APP-07: the application queue — repo, orchestrator-backed preparer,
        // and the single-flight worker (started lazily on first enqueue).
        services.AddSingleton<IApplicationQueueRepository, ApplicationQueueRepository>();
        services.AddSingleton<IApplicationPreparer, OrchestratorApplicationPreparer>();
        // APP-12: submitter + Full-Auto gate (the worker only submits for
        // policy-allowed sites; enforcement re-checks at gate time anyway).
        services.AddSingleton<IApplicationSubmitter, OrchestratorApplicationSubmitter>();
        services.AddSingleton<IFullAutoGate, QueueFullAutoGate>();
        services.AddSingleton<ApplicationQueueService>();
        services.AddSingleton<Func<EnvoySettings>>(sp => sp.GetRequiredService<EnvoySettings>);

        return services;
    }
}