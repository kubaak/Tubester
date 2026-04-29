Implement backend support for runtime application configuration in Tubester.

Context:
- Backend is ASP.NET Core / .NET.
- Entity Framework Core is used.
- Existing style prefers clean services, DTOs, controllers, migrations, and tests.
- This feature is for configuration that can be changed without deployment.
- Do NOT store secrets in this table.
- Main first use case: selecting which AI provider/model Tubester should use.
- Also intended for feature toggles.

Implement:

1. New entity/table: ApplicationConfiguration

Columns:
- Key: string, primary key, max length 200
- Value: string, required
- ValueType: enum/string, required
    - String
    - Boolean
    - Integer
    - Decimal
    - Json
- Description: string?, nullable
- IsSystem: bool, required
- UpdatedAtUtc: DateTime, required

Use Key instead of Type.

Example keys:
- Ai.Provider
- Ai.Model
- Feature.PlaylistSuggest
- Feature.AutoReplies

2. Add EF Core mapping

Requirements:
- Key is primary key.
- Key max length 200.
- Value is required.
- ValueType should be stored as string.
- IsSystem required.
- UpdatedAtUtc required.
- Add migration.

3. Add seed/default data

Seed these system configurations:

- Key: Ai.Provider
  Value: Ollama
  ValueType: String
  Description: Current AI provider used by Tubester
  IsSystem: true

- Key: Ai.Model
  Value: qwen3:8b
  ValueType: String
  Description: Default AI model used by Tubester
  IsSystem: true

- Key: Feature.PlaylistSuggest
  Value: true
  ValueType: Boolean
  Description: Enables AI playlist suggestions
  IsSystem: true

- Key: Feature.AutoReplies
  Value: false
  ValueType: Boolean
  Description: Enables automatic replies
  IsSystem: true

4. Add constants

Create something like:

ApplicationConfigurationKeys

with constants:
- AiProvider = "Ai.Provider"
- AiModel = "Ai.Model"
- FeaturePlaylistSuggest = "Feature.PlaylistSuggest"
- FeatureAutoReplies = "Feature.AutoReplies"

Also add AiProviders constants:
- Ollama
- OpenAi

5. Add service for reading configuration

Create:

IApplicationConfigurationService

Methods:
- Task<string?> GetValueAsync(string key, CancellationToken ct)
- Task<T?> GetValueAsync<T>(string key, CancellationToken ct)
- Task<IReadOnlyList<ApplicationConfigurationDto>> GetAllAsync(CancellationToken ct)
- Task<ApplicationConfigurationDto?> GetByKeyAsync(string key, CancellationToken ct)
- Task<ApplicationConfigurationDto> CreateAsync(CreateApplicationConfigurationRequest request, CancellationToken ct)
- Task<ApplicationConfigurationDto?> UpdateAsync(string key, UpdateApplicationConfigurationRequest request, CancellationToken ct)
- Task<bool> DeleteAsync(string key, CancellationToken ct)

Implementation:
- Use EF Core.
- Use AsNoTracking for reads.
- Cache individual key lookups using IMemoryCache.
- Cache duration: 1 minute.
- Invalidate/remove cache entry after create/update/delete.
- Normalize keys with Trim().
- Validate that Key is not empty.
- Validate that Value matches ValueType:
    - Boolean must parse as bool
    - Integer must parse as int
    - Decimal must parse as decimal invariant culture
    - Json must be valid JSON
    - String accepts anything non-null
- Do not allow deleting IsSystem=true records.
- Do not allow changing Key on update.
- On update, update Value, ValueType, Description, UpdatedAtUtc.

6. Add feature flag service

Create:

IFeatureFlagService

Method:
- Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct)

Behavior:
- If featureKey already starts with "Feature.", use it as-is.
- Otherwise prefix with "Feature."
- Return false if key does not exist.
- Parse boolean values safely.

7. Add AI runtime options service

Create:

AiRuntimeOptions record:
- string Provider
- string Model

Create:

IAiRuntimeOptionsService

Method:
- Task<AiRuntimeOptions> GetAsync(CancellationToken ct)

Behavior:
- Reads Ai.Provider and Ai.Model from ApplicationConfigurationService.
- Defaults:
    - Provider = Ollama
    - Model = qwen3:8b
- Validate provider is one of registered AiProviders.
- Throw clear InvalidOperationException for unsupported provider.

8. Add AI client strategy/factory

Refactor AI client resolution so consumers do not inject a single IAiClient directly.

Add:

IAiClientFactory

Method:
- Task<IAiClient> GetClientAsync(CancellationToken ct)
  or preferably:
- Task<(IAiClient Client, AiRuntimeOptions Options)> GetClientAsync(CancellationToken ct)

Each IAiClient implementation should expose:
- string Provider { get; }

Factory:
- Inject IEnumerable<IAiClient>
- Inject IAiRuntimeOptionsService
- Resolve client by Provider
- Throw clear InvalidOperationException if configured provider has no registered implementation.

Do not create one client per model. Client is per provider. Model is part of runtime options.

Update existing AI usage so it gets the client through IAiClientFactory and passes runtime options/model where needed.

9. Add admin API endpoints

Controller route:

/api/application-configurations

Endpoints:
- GET /api/application-configurations
- GET /api/application-configurations/{key}
- POST /api/application-configurations
- PUT /api/application-configurations/{key}
- DELETE /api/application-configurations/{key}

DTOs:
- ApplicationConfigurationDto
- CreateApplicationConfigurationRequest
- UpdateApplicationConfigurationRequest

Behavior:
- GET all ordered by Key.
- GET by key returns 404 if missing.
- POST creates config, returns 201.
- POST should return 409 if key already exists.
- PUT updates config, returns 404 if missing.
- DELETE returns 204 if deleted.
- DELETE returns 400 if IsSystem=true.
- Use cancellation tokens.
- Use ProducesResponseType attributes consistent with existing project style.
- Protect endpoints with the existing admin authorization policy if one exists. If no admin policy exists, add a TODO comment and use Authorize for now.

10. Register all services in DI

Register:
- IApplicationConfigurationService
- IFeatureFlagService
- IAiRuntimeOptionsService
- IAiClientFactory
- all IAiClient implementations as IAiClient

11. Add tests

Add unit/integration tests for:
- Reading existing configuration.
- Returning defaults for AI runtime options when missing.
- Rejecting invalid boolean/integer/decimal/json values.
- Updating config invalidates cache.
- System config cannot be deleted.
- Feature flag returns false when missing.
- Feature flag parses true/false.
- AI client factory resolves correct provider.
- AI client factory throws when provider is unsupported or not registered.
- API GET/PUT/POST/DELETE behavior if API integration tests exist.

Important style requirements:
- Keep implementation simple.
- Do not over-engineer rollout percentages yet.
- Do not add secrets support.
- Do not store API keys in ApplicationConfigurations.
- Avoid leaking raw DB entities from controllers.
- Prefer DTOs.
- Prefer explicit exceptions/messages over silent fallback for invalid configured provider.
- Follow existing Tubester naming/folder conventions.