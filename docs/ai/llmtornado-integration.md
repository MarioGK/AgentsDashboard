# LlmTornado Integration (Z.ai, GLM-5)

## Package
- NuGet package: `LlmTornado` (current repo pin: `3.8.47`).

## Official Model Support
- LLMTornado model enum includes:
  - `ChatModel.Zai.Glm.Glm4_5`
  - `ChatModel.Zai.Glm.Glm4_5_Air`
  - `ChatModel.Zai.Glm.Glm4_5_X`
  - `ChatModel.Zai.Glm.Glm5`

## Preferred Runtime Pattern
Use native Z.ai provider with fixed `glm-5`:

```csharp
var api = new TornadoApi(LLmProviders.Zai, apiKey);
var response = await api.Chat
    .CreateConversation(ChatModel.Zai.Glm.Glm5, temperature: 0.2, maxTokens: 4096)
    .AppendSystemMessage("System prompt")
    .AppendUserInput("User prompt")
    .GetResponse();
```

## Anthropic-Compatible Endpoint Mode
When tooling expects Anthropic semantics, LLMTornado supports custom endpoint providers:

```csharp
var api = new TornadoApi(new AnthropicEndpointProvider
{
    Auth = new ProviderAuthentication("YOUR_KEY"),
    UrlResolver = (endpoint, url, ctx) => "https://api.z.ai/api/anthropic/v1/{0}{1}"
});
```

## AgentsDashboard Usage
- Central service: `LlmTornadoGatewayService`.
- Current feature integrations:
  - AI Dockerfile generation in `ImageBuilder`.
  - AI task prompt generation in `RepositoryDetail`.
- Policy: hard-require `glm-5`; no fallback to earlier GLM families.
