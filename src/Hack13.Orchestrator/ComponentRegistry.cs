using Hack13.Calculator;
using Hack13.Contracts.Interfaces;
using Hack13.DatabaseReader;
using Hack13.DecisionEngine;
using Hack13.EmailSender;
using Hack13.TerminalClient;
using Hack13.PdfGenerator;

namespace Hack13.Orchestrator;

public sealed class ComponentRegistry
{
    private readonly Dictionary<string, Func<IComponent>> _registry =
        new(StringComparer.OrdinalIgnoreCase);

    public ComponentRegistry Register(string componentType, Func<IComponent> factory)
    {
        if (string.IsNullOrWhiteSpace(componentType))
            throw new ArgumentException("componentType is required.", nameof(componentType));

        _registry[componentType] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public bool IsRegistered(string componentType) =>
        !string.IsNullOrWhiteSpace(componentType) && _registry.ContainsKey(componentType);

    public IComponent Create(string componentType)
    {
        if (!_registry.TryGetValue(componentType, out var factory))
            throw new KeyNotFoundException($"Component type '{componentType}' is not registered.");

        return factory();
    }

    public static ComponentRegistry CreateDefault(EmailSenderEnvironmentConfig? emailEnvironmentConfig = null)
    {
        var envConfig = emailEnvironmentConfig ?? new EmailSenderEnvironmentConfig();
        var emailTransport = EmailTransportFactory.Create(envConfig.Transport);

        return new ComponentRegistry()
            .Register("green_screen_connector", () => new Hack13.TerminalClient.GreenScreenConnector())
            .Register("calculate", () => new CalculatorComponent())
            .Register("decision", () => new DecisionEngineComponent())
            .Register("pdf_generator", () => new PdfGeneratorComponent())
            .Register("email_sender", () => new EmailSenderComponent(emailTransport, envConfig))
            .Register("database_reader", () => new DatabaseReaderComponent());
    }
}
