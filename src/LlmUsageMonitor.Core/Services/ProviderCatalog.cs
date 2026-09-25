using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Core.Services;

/// <summary>Static metadata for every supported platform, shared by UI, adapters and pricing.</summary>
public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderDescriptor> All = new List<ProviderDescriptor>
    {
        new()
        {
            Kind = ProviderKind.OpenAI, DisplayName = "OpenAI",
            DefaultBaseUrl = "https://api.openai.com/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = true,
            OfficialUsageApi = "openai", DefaultColorHex = "#10A37F",
            ConsoleUrl = "https://platform.openai.com/usage",
            DocsUrl = "https://platform.openai.com/docs/api-reference/usage"
        },
        new()
        {
            Kind = ProviderKind.OpenRouter, DisplayName = "OpenRouter",
            DefaultBaseUrl = "https://openrouter.ai/api/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = true,
            OfficialUsageApi = "openrouter", DefaultColorHex = "#6467F2",
            ConsoleUrl = "https://openrouter.ai/activity",
            DocsUrl = "https://openrouter.ai/docs/api-reference/overview"
        },
        new()
        {
            Kind = ProviderKind.Anthropic, DisplayName = "Anthropic Claude",
            DefaultBaseUrl = "https://api.anthropic.com/v1",
            OpenAiCompatible = false, SupportsOfficialUsage = true,
            OfficialUsageApi = "anthropic", DefaultColorHex = "#D97757",
            ConsoleUrl = "https://console.anthropic.com/settings/usage",
            DocsUrl = "https://docs.anthropic.com/en/api/usage-cost-api"
        },
        new()
        {
            Kind = ProviderKind.DeepSeek, DisplayName = "DeepSeek 深度求索",
            DefaultBaseUrl = "https://api.deepseek.com/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "balance", DefaultColorHex = "#4D6BFE",
            ConsoleUrl = "https://platform.deepseek.com/usage",
            Note = "官方仅提供余额接口；用量可用本地代理或网页登录抓取。"
        },
        new()
        {
            Kind = ProviderKind.Moonshot, DisplayName = "Moonshot 月之暗面",
            DefaultBaseUrl = "https://api.moonshot.cn/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "balance", DefaultColorHex = "#16B8A8",
            ConsoleUrl = "https://platform.moonshot.cn/console/info",
            Note = "官方仅提供余额接口；用量可用本地代理或网页登录抓取。"
        },
        new()
        {
            Kind = ProviderKind.Zhipu, DisplayName = "Zhipu 智谱 GLM",
            DefaultBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "none", DefaultColorHex = "#3B6FE0",
            ConsoleUrl = "https://open.bigmodel.cn/console/overview"
        },
        new()
        {
            Kind = ProviderKind.AlibabaDashScope, DisplayName = "阿里百炼 DashScope",
            DefaultBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "none", DefaultColorHex = "#FF6A00",
            ConsoleUrl = "https://bailian.console.aliyun.com/"
        },
        new()
        {
            Kind = ProviderKind.VolcengineArk, DisplayName = "火山方舟 Ark",
            DefaultBaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "none", DefaultColorHex = "#1664FF",
            ConsoleUrl = "https://console.volcengine.com/ark"
        },
        new()
        {
            Kind = ProviderKind.SiliconFlow, DisplayName = "SiliconFlow 硅基流动",
            DefaultBaseUrl = "https://api.siliconflow.cn/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "balance", DefaultColorHex = "#6D28D9",
            ConsoleUrl = "https://cloud.siliconflow.cn/account/balance"
        },
        new()
        {
            Kind = ProviderKind.GoogleGemini, DisplayName = "Google Gemini",
            DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "none", DefaultColorHex = "#4285F4"
        },
        new()
        {
            Kind = ProviderKind.AzureOpenAI, DisplayName = "Azure OpenAI",
            DefaultBaseUrl = "https://{resource}.openai.azure.com",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "none", DefaultColorHex = "#0078D4"
        },
        new()
        {
            Kind = ProviderKind.Custom, DisplayName = "自定义 / 兼容 OpenAI",
            DefaultBaseUrl = "http://localhost:11434/v1",
            OpenAiCompatible = true, SupportsOfficialUsage = false,
            OfficialUsageApi = "balance", DefaultColorHex = "#8E9BAE"
        }
    };

    private static readonly Dictionary<ProviderKind, string[]> Aliases = new()
    {
        [ProviderKind.OpenAI] = new[] { "openai" },
        [ProviderKind.OpenRouter] = new[] { "openrouter" },
        [ProviderKind.Anthropic] = new[] { "anthropic", "claude" },
        [ProviderKind.DeepSeek] = new[] { "deepseek" },
        [ProviderKind.Moonshot] = new[] { "moonshot", "kimi" },
        [ProviderKind.Zhipu] = new[] { "zhipu", "zhipuai", "glm", "bigmodel" },
        [ProviderKind.AlibabaDashScope] = new[] { "dashscope", "alibaba", "qwen", "aliyun" },
        [ProviderKind.VolcengineArk] = new[] { "volcengine", "ark", "doubao", "bytedance" },
        [ProviderKind.SiliconFlow] = new[] { "siliconflow", "silicon" },
        [ProviderKind.GoogleGemini] = new[] { "gemini", "google", "vertex_ai", "palm" },
        [ProviderKind.AzureOpenAI] = new[] { "azure", "azure_openai", "azureopenai" },
        [ProviderKind.Custom] = new[] { "custom", "local", "ollama", "openai-compatible" }
    };

    public static ProviderDescriptor Get(ProviderKind kind)
        => All.FirstOrDefault(d => d.Kind == kind) ?? All[^1];

    public static IEnumerable<string> AliasesFor(ProviderKind kind)
        => Aliases.TryGetValue(kind, out var a) ? a : new[] { kind.ToString().ToLowerInvariant() };

    /// <summary>Maps a pricing-source provider string (e.g. litellm "dashscope") back to a kind.</summary>
    public static ProviderKind? FromAlias(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName)) return null;
        var name = providerName.Trim().ToLowerInvariant();
        foreach (var (kind, aliases) in Aliases)
        {
            if (aliases.Any(a => a == name)) return kind;
        }
        if (Enum.TryParse<ProviderKind>(providerName, ignoreCase: true, out var parsed)) return parsed;
        return null;
    }
}
