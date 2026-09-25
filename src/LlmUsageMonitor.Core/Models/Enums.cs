namespace LlmUsageMonitor.Core.Models;

/// <summary>Supported large-model platforms. Custom covers any OpenAI-compatible endpoint.</summary>
public enum ProviderKind
{
    OpenAI,
    OpenRouter,
    Anthropic,
    DeepSeek,
    Moonshot,
    Zhipu,
    AlibabaDashScope,
    VolcengineArk,
    SiliconFlow,
    GoogleGemini,
    AzureOpenAI,
    Custom
}

/// <summary>Where a usage record came from.</summary>
public enum UsageSource
{
    OfficialApi,
    LocalProxy,
    Import,
    Manual
}

public enum Currency
{
    USD,
    CNY
}

/// <summary>Cache buckets, each with its own TTL policy.</summary>
public enum CacheTier
{
    Pricing,
    ExchangeRate,
    Usage,
    Models,
    Generic
}

public enum BudgetScopeType
{
    Global,
    Group,
    Credential
}

public enum BudgetPeriod
{
    Daily,
    Monthly
}

public enum AlertLevel
{
    Info,
    Warning,
    Critical
}

public enum ReportFormat
{
    Csv,
    Excel,
    Json
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum ProxyMode
{
    None,
    System,
    Custom
}

public enum ProxyProtocol
{
    Http,
    Socks5
}

/// <summary>How usage for a credential is collected.</summary>
public enum CollectionMode
{
    None,
    OfficialApi,
    LocalProxy,
    Both
}
