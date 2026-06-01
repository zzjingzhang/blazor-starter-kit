# 语言偏好链路追踪与潜在缺陷分析

## 1. 语言偏好完整链路概览

语言偏好系统分为**客户端（WebAssembly）**与**服务端（ASP.NET Core）**两条独立但又交互的链路。下图展示从用户触发语言切换到最终文化生效的全流程：

```
用户切换语言
    │
    ├──────────────────────────────────────────────────────────┐
    │ 客户端链路                                                │ 服务端链路
    ▼                                                          ▼
ClientPreferenceManager.ChangeLanguageAsync()        PreferencesController.ChangeLanguageAsync()
    │                                                          │
    ▼                                                          ▼
ILocalStorageService.SetItemAsync()                  ServerPreferenceManager.ChangeLanguageAsync()
    │                                                          │
    ▼                                                          ▼
浏览器 localStorage                                   IServerStorageService.SetItemAsync()
    │                                                          │
    ▼                                                          ▼
下次启动时 Client.Program.Main 读取                    ServerStorageService → ServerStorageProvider
    │                                                          │
    ▼                                                          ▼
CultureInfo.DefaultThreadCurrentCulture              私有 Dictionary<string, string>（内存）
    │
    ▼
HttpClient Accept-Language 请求头
    │
    ▼
RequestCultureMiddleware（服务端中间件）
    │
    ▼
CultureInfo.CurrentCulture / CurrentUICulture
```

---

## 2. 客户端启动时序分析

### 2.1 `Client.Program.Main` 启动流程

文件：[Program.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Program.cs#L15-L35)

```csharp
public static async Task Main(string[] args)
{
    var builder = WebAssemblyHostBuilder
                  .CreateDefault(args)
                  .AddRootComponents()
                  .AddClientServices();           // ① 注册服务（含 HttpClient Accept-Language 配置）
    var host = builder.Build();                    // ② 第一次 Build
    var storageService = host.Services.GetRequiredService<ClientPreferenceManager>();
    if (storageService != null)
    {
        CultureInfo culture;
        var preference = await storageService.GetPreference() as ClientPreference;  // ③ 从 localStorage 读取偏好
        if (preference != null)
            culture = new CultureInfo(preference.LanguageCode);
        else
            culture = new CultureInfo(LocalizationConstants.SupportedLanguages.FirstOrDefault()?.Code ?? "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;     // ④ 设置全局默认文化
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    await builder.Build().RunAsync();               // ⑤ 第二次 Build（实际运行）
}
```

### 2.2 `AddClientServices` 中 Accept-Language 注册时机

文件：[WebAssemblyHostBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Extensions/WebAssemblyHostBuilderExtensions.cs#L35-L75)

```csharp
public static WebAssemblyHostBuilder AddClientServices(this WebAssemblyHostBuilder builder)
{
    builder.Services
        .AddLocalization(...)
        .AddBlazoredLocalStorage()
        .AddScoped<ClientPreferenceManager>()
        // ...
        .AddHttpClient(ClientName, client =>
        {
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
                CultureInfo.DefaultThreadCurrentCulture?.TwoLetterISOLanguageName);  // ← 此处读取的是注册时的 DefaultThreadCurrentCulture
            client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
        })
        .AddHttpMessageHandler<AuthenticationHeaderHandler>();
    // ...
}
```

### 2.3 时序对比：关键矛盾

| 步骤 | 操作 | `DefaultThreadCurrentCulture` 状态 |
|------|------|-----------------------------------|
| ① | `AddClientServices()` 注册 `HttpClient`，读取 `DefaultThreadCurrentCulture` 写入 `Accept-Language` | **尚未设置**（为 `null`） |
| ② | `builder.Build()` — 第一次构建 Host | 仍为 `null` |
| ③ | 从 `localStorage` 读取 `ClientPreference.LanguageCode` | 仍为 `null` |
| ④ | 设置 `CultureInfo.DefaultThreadCurrentCulture` | **此时才设置** |
| ⑤ | `builder.Build().RunAsync()` — 第二次构建并运行 | 已设置，但 HttpClient 早已注册完毕 |

**结论**：`AddClientServices` 在步骤 ① 中将 `Accept-Language` 注册为 `HttpClient` 的默认请求头，此时 `DefaultThreadCurrentCulture` 尚未被从 `localStorage` 读取的偏好值覆盖，因此 `HttpClient` 的 `Accept-Language` 始终基于默认文化（通常为系统区域设置或 `null`），**无法反映用户存储的语言偏好**。

---

## 3. 客户端偏好管理：`ClientPreferenceManager`

文件：[ClientPreferenceManager.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client.Infrastructure/Managers/Preferences/ClientPreferenceManager.cs#L1-L100)

### 3.1 数据模型

[ClientPreference](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client.Infrastructure/Settings/ClientPreference.cs#L7-L14) 是一个 `record`，实现 [IPreference](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Settings/IPreference.cs#L3-L6)：

```csharp
public record ClientPreference : IPreference
{
    public bool IsDarkMode { get; set; }
    public bool IsRTL { get; set; }
    public bool IsDrawerOpen { get; set; }
    public string PrimaryColor { get; set; }
    public string LanguageCode { get; set; } = LocalizationConstants.SupportedLanguages.FirstOrDefault()?.Code ?? "en-US";
}
```

### 3.2 读取偏好

```csharp
public async Task<IPreference> GetPreference()
{
    return await _localStorageService.GetItemAsync<ClientPreference>(StorageConstants.Local.Preference) ?? new ClientPreference();
}
```

- 存储键：`StorageConstants.Local.Preference` = `"clientPreference"`
- 使用 `Blazored.LocalStorage.ILocalStorageService` 访问浏览器 `localStorage`
- 若无数据则返回默认的 `ClientPreference`（`LanguageCode` 默认为 `"en-US"`）

### 3.3 切换语言

```csharp
public async Task<IResult> ChangeLanguageAsync(string languageCode)
{
    var preference = await GetPreference() as ClientPreference;
    if (preference != null)
    {
        preference.LanguageCode = languageCode;
        await SetPreference(preference);  // → _localStorageService.SetItemAsync("clientPreference", preference)
        return new Result { Succeeded = true, Messages = ... };
    }
    return new Result { Succeeded = false, Messages = ... };
}
```

**特点**：
- 仅写入客户端 `localStorage`，**不通知服务端**
- 切换后需刷新页面才能通过 `Program.Main` 重新读取并生效

---

## 4. 服务端偏好管理：`ServerPreferenceManager`

文件：[ServerPreferenceManager.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Managers/Preferences/ServerPreferenceManager.cs#L1-L56)

### 4.1 数据模型

[ServerPreference](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Settings/ServerPreference.cs#L6-L10)：

```csharp
public record ServerPreference : IPreference
{
    public string LanguageCode { get; set; } = LocalizationConstants.SupportedLanguages.FirstOrDefault()?.Code ?? "en-US";
}
```

### 4.2 读取偏好

```csharp
public async Task<IPreference> GetPreference()
{
    return await _serverStorageService.GetItemAsync<ServerPreference>(StorageConstants.Server.Preference) ?? new ServerPreference();
}
```

- 存储键：`StorageConstants.Server.Preference` = `"serverPreference"`
- 使用 `IServerStorageService` → `ServerStorageService` → `ServerStorageProvider`

### 4.3 切换语言

```csharp
public async Task<IResult> ChangeLanguageAsync(string languageCode)
{
    var preference = await GetPreference() as ServerPreference;
    if (preference != null)
    {
        preference.LanguageCode = languageCode;
        await SetPreference(preference);  // → _serverStorageService.SetItemAsync("serverPreference", preference)
        return new Result { Succeeded = true, Messages = ... };
    }
    return new Result { Succeeded = false, Messages = ... };
}
```

---

## 5. API 端点：`PreferencesController`

文件：[PreferencesController.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/PreferencesController.cs#L1-L35)

```csharp
[Route("api/[controller]")]
[ApiController]
public class PreferencesController : ControllerBase
{
    private readonly ServerPreferenceManager _serverPreferenceManager;

    [Authorize(Policy = Permissions.Preferences.ChangeLanguage)]  // ← 需要权限声明
    [HttpPost("changeLanguage")]
    public async Task<IActionResult> ChangeLanguageAsync(string languageCode)
    {
        var result = await _serverPreferenceManager.ChangeLanguageAsync(languageCode);
        return Ok(result);
    }
}
```

### 5.1 权限要求

- 端点标记 `[Authorize(Policy = "Permissions.Preferences.ChangeLanguage")]`
- 该权限定义在 [Permissions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs#L113-L118)：
  ```csharp
  public static class Preferences
  {
      public const string ChangeLanguage = "Permissions.Preferences.ChangeLanguage";
  }
  ```
- 用户必须在 JWT 的 `Permission` claim 中包含 `"Permissions.Preferences.ChangeLanguage"` 才能调用此端点
- 权限策略在服务端 `ServiceCollectionExtensions.AddJwtAuthentication` 和客户端 `WebAssemblyHostBuilderExtensions.AddClientServices` 中均通过反射注册

---

## 6. 请求文化中间件：`RequestCultureMiddleware`

文件：[RequestCultureMiddleware.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Middlewares/RequestCultureMiddleware.cs#L1-L42)

```csharp
public async Task InvokeAsync(HttpContext context)
{
    var cultureQuery = context.Request.Query["culture"];
    if (!string.IsNullOrWhiteSpace(cultureQuery))
    {
        var culture = new CultureInfo(cultureQuery);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
    else if (context.Request.Headers.ContainsKey("Accept-Language"))
    {
        var cultureHeader = context.Request.Headers["Accept-Language"];
        if (cultureHeader.Any())
        {
            var culture = new CultureInfo(cultureHeader.First().Split(',').First().Trim());
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
    await _next(context);
}
```

### 6.1 优先级

1. **`?culture=xxx` 查询串**（最高优先级）
2. **`Accept-Language` 请求头**（降级策略）

### 6.2 注册位置

在 [ApplicationBuilderExtensions.UseRequestLocalizationByCulture](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ApplicationBuilderExtensions.cs#L65-L79) 中注册：

```csharp
internal static IApplicationBuilder UseRequestLocalizationByCulture(this IApplicationBuilder app)
{
    var supportedCultures = LocalizationConstants.SupportedLanguages.Select(l => new CultureInfo(l.Code)).ToArray();
    app.UseRequestLocalization(options =>
    {
        options.SupportedUICultures = supportedCultures;
        options.SupportedCultures = supportedCultures;
        options.DefaultRequestCulture = new RequestCulture(supportedCultures.First());
        options.ApplyCurrentCultureToResponseHeaders = true;
    });
    app.UseMiddleware<RequestCultureMiddleware>();
    return app;
}
```

在 [Startup.Configure](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Startup.cs#L83) 中调用 `app.UseRequestLocalizationByCulture()`。

---

## 7. 服务配置期文化设置：`SetCultureFromServerPreferenceAsync`

文件：[ServiceCollectionExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L57-L118)

### 7.1 调用链

```
RegisterSwagger()
  → AddSwaggerGen(async c => { var localizer = await GetRegisteredServerLocalizerAsync<ServerCommonResources>(services); })

AddJwtAuthentication()
  → AddJwtBearer(async bearer => { var localizer = await GetRegisteredServerLocalizerAsync<ServerCommonResources>(services); })

GetRegisteredServerLocalizerAsync<T>()
  → services.BuildServiceProvider()
  → SetCultureFromServerPreferenceAsync(serviceProvider)
  → serviceProvider.GetService<IStringLocalizer<T>>()
  → serviceProvider.DisposeAsync()
```

### 7.2 核心方法

```csharp
private static async Task SetCultureFromServerPreferenceAsync(IServiceProvider serviceProvider)
{
    var storageService = serviceProvider.GetService<ServerPreferenceManager>();
    if (storageService != null)
    {
        CultureInfo culture;
        if (await storageService.GetPreference() is ServerPreference preference)
            culture = new(preference.LanguageCode);
        else
            culture = new(LocalizationConstants.SupportedLanguages.FirstOrDefault()?.Code ?? "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
```

### 7.3 问题：临时 ServiceProvider

`GetRegisteredServerLocalizerAsync` 每次调用都会 `BuildServiceProvider()` 创建一个临时容器，用于在**服务配置阶段**（`ConfigureServices` 内）获取 `IStringLocalizer`，以支持 Swagger 描述和 JWT 错误消息的本地化。该临时容器在获取 `Localizer` 后立即 `DisposeAsync()`。

---

## 8. 服务端存储层：`ServerStorageProvider`

文件：[ServerStorageProvider.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Storage/Provider/ServerStorageProvider.cs#L1-L129)

### 8.1 注册方式

在 [Infrastructure/ServiceCollectionExtensions.AddServerStorage](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Extensions/ServiceCollectionExtensions.cs#L42-L58) 中：

```csharp
public static IServiceCollection AddServerStorage(this IServiceCollection services, Action<SystemTextJsonOptions> configure)
{
    return services
        .AddScoped<IJsonSerializer, SystemTextJsonSerializer>()
        .AddScoped<IStorageProvider, ServerStorageProvider>()          // Scoped 生命周期
        .AddScoped<IServerStorageService, ServerStorageService>()
        .AddScoped<ISyncServerStorageService, ServerStorageService>()
        ...
}
```

### 8.2 实现状态

| 方法 | 异步版本 | 同步版本 | 状态 |
|------|---------|---------|------|
| `SetItem` | ✅ 使用 `_storage` 字典 | ❌ `NotImplementedException` | 异步写入内存字典 |
| `GetItem` | ✅ 使用 `_storage` 字典 | ❌ `NotImplementedException` | 异步读取内存字典 |
| `RemoveItem` | ❌ `NotImplementedException` | ❌ `NotImplementedException` | 不可用 |
| `Clear` | ❌ `NotImplementedException` | ❌ `NotImplementedException` | 不可用 |
| `ContainKey` | ❌ `NotImplementedException` | ❌ `NotImplementedException` | 不可用 |
| `Length` | ❌ `NotImplementedException` | ❌ `NotImplementedException` | 不可用 |
| `Key` | ❌ `NotImplementedException` | ❌ `NotImplementedException` | 不可用 |

---

## 9. 潜在缺陷一：`builder.Build()` 被调用两次

### 9.1 问题代码

[Program.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Program.cs#L21-L34)：

```csharp
var host = builder.Build();              // 第一次 Build
// ... 从 host.Services 获取 ClientPreferenceManager 并设置文化 ...
await builder.Build().RunAsync();        // 第二次 Build
```

### 9.2 缺陷分析

#### （1）两个独立的 DI 容器

`WebAssemblyHostBuilder.Build()` 每次调用都会创建一个全新的 `WebAssemblyHost`，内部包含独立的 `IServiceProvider`。这意味着：

- **第一个 `host`**：用于读取偏好和设置文化，其中 `ClientPreferenceManager`、`ILocalStorageService` 等 Scoped 服务实例属于该容器的作用域
- **第二个 `builder.Build()`**：创建运行时实际使用的 Host，所有 Scoped 服务（包括 `ClientPreferenceManager`、`BlazorHeroStateProvider`、`IHttpClientFactory` 等）是全新的实例

第一个 Host 从未被 `Dispose`，其服务实例可能持有资源（如 `ILocalStorageService` 的 JS 互操作引用），造成潜在的资源泄漏。

#### （2）Accept-Language 请求头始终为默认值

这是最严重的连锁影响。`AddClientServices` 在服务注册阶段（`Build` 之前）配置 `HttpClient`：

```csharp
.AddHttpClient(ClientName, client =>
{
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
        CultureInfo.DefaultThreadCurrentCulture?.TwoLetterISOLanguageName);
})
```

此时 `DefaultThreadCurrentCulture` 尚未被从 `localStorage` 读取的偏好值覆盖（为 `null` 或系统默认），因此：

- `CultureInfo.DefaultThreadCurrentCulture?.TwoLetterISOLanguageName` 的结果是 `null`
- `Accept-Language` 头将不会被正确设置为用户的偏好语言
- 即使后续 `Program.Main` 设置了 `DefaultThreadCurrentCulture`，已注册的 `HttpClient` 默认头不会自动更新
- 客户端发往服务端的所有 API 请求携带的 `Accept-Language` 均不反映用户偏好
- 服务端 `RequestCultureMiddleware` 依赖 `Accept-Language` 头来设置请求文化，因此服务端的文化也会回退到默认值

#### （3）正确的修复方案

应只 `Build` 一次，并在 `Build` 之后设置文化，然后利用 `IHttpClientFactory` 动态设置请求头，或使用 `DelegatingHandler` 在每次请求时读取当前文化：

```csharp
public static async Task Main(string[] args)
{
    var builder = WebAssemblyHostBuilder
                  .CreateDefault(args)
                  .AddRootComponents()
                  .AddClientServices();
    var host = builder.Build();

    var storageService = host.Services.GetRequiredService<ClientPreferenceManager>();
    if (storageService != null)
    {
        CultureInfo culture;
        var preference = await storageService.GetPreference() as ClientPreference;
        if (preference != null)
            culture = new CultureInfo(preference.LanguageCode);
        else
            culture = new CultureInfo(LocalizationConstants.SupportedLanguages.FirstOrDefault()?.Code ?? "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    await host.RunAsync();  // 复用同一个 host
}
```

同时应将 `HttpClient` 的 `Accept-Language` 注册改为动态 `DelegatingHandler`：

```csharp
public class CultureHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.ParseAdd(
            CultureInfo.DefaultThreadCurrentCulture?.TwoLetterISOLanguageName);
        return base.SendAsync(request, cancellationToken);
    }
}
```

---

## 10. 潜在缺陷二：`ServerStorageProvider` 使用私有内存 `Dictionary` 且大量方法 `NotImplemented`

### 10.1 问题代码

[ServerStorageProvider.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Storage/Provider/ServerStorageProvider.cs#L9-L128)：

```csharp
internal class ServerStorageProvider : IStorageProvider
{
    private Dictionary<string, string> _storage = new();  // 实例级私有字典
    // ...
}
```

注册为 `Scoped` 生命周期：

```csharp
services.AddScoped<IStorageProvider, ServerStorageProvider>();
```

### 10.2 缺陷分析

#### （1）数据不可跨请求持久化

`ServerStorageProvider` 注册为 **Scoped** 生命周期，每个 HTTP 请求都会创建一个新的 `ServerStorageProvider` 实例，其内部的 `_storage` 字典是全新的空字典。这意味着：

- `ServerPreferenceManager.ChangeLanguageAsync()` 写入的语言偏好**在同一请求结束后即丢失**
- `ServerPreferenceManager.GetPreference()` 在新请求中**永远返回默认的 `ServerPreference`**（`LanguageCode = "en-US"`）
- `SetCultureFromServerPreferenceAsync` 在服务配置阶段读取到的也是空字典，无法获取之前设置的偏好

**根本原因**：`_storage` 是实例字段而非共享静态字段或外部持久化存储。在 Scoped 生命周期下，每个请求的 `ServerStorageProvider` 是独立的，数据天然无法跨请求共享。

#### （2）`IStorageProvider` 接口大面积未实现

`IStorageProvider` 接口定义了 14 个方法（7 对同步/异步），但 `ServerStorageProvider` 只实现了 2 个异步方法（`SetItemAsync`、`GetItemAsync`），其余 12 个方法全部抛出 `NotImplementedException`：

| 未实现方法 | 影响 |
|-----------|------|
| `RemoveItemAsync` / `RemoveItem` | 无法删除偏好键值，无法重置语言设置 |
| `ClearAsync` / `Clear` | 无法清空存储，无法批量重置 |
| `ContainKeyAsync` / `ContainKey` | 无法检查键是否存在，影响条件逻辑 |
| `LengthAsync` / `Length` | 无法获取存储项数量 |
| `KeyAsync` / `Key` | 无法按索引枚举键 |
| `SetItem` / `GetItem`（同步） | `ServerStorageService` 的同步 API 全部不可用 |

这意味着：
- 任何调用 `IServerStorageService.RemoveItemAsync()` 的代码都会抛出运行时异常
- `ServerStorageService` 的同步方法（如 `SetItem<T>`、`GetItem<T>`）通过调用 `IStorageProvider` 的同步方法，同样会抛出 `NotImplementedException`
- 偏好管理的"删除"或"重置"功能完全不可用

#### （3）对测试的影响

- 单元测试若需要调用 `ContainKeyAsync`、`RemoveItemAsync`、`ClearAsync` 等方法，将直接抛出 `NotImplementedException`，导致测试无法运行
- 由于 `_storage` 是私有字段，测试无法通过常规方式验证内部状态
- 测试中若使用 `ServerStorageService` 的同步 API（如 `GetItem<T>`），同样会遇到 `NotImplementedException`
- 无法编写集成测试验证"跨请求持久化"行为，因为当前实现根本不支持

#### （4）修复方向

- **短期**：将 `_storage` 改为 `static` 字典，使数据在同一进程内跨请求共享（至少实现 Scoped 实例间的数据可见性）
- **中期**：实现所有 `IStorageProvider` 方法，使用数据库或文件系统作为后端存储
- **长期**：引入分布式缓存（如 Redis）或数据库表，实现真正的服务端偏好持久化，支持多实例部署场景

---

## 11. 链路时序总结

```
┌─────────────────────────── 客户端启动时序 ───────────────────────────┐
│                                                                      │
│  AddClientServices()                                                  │
│    ├── 注册 ClientPreferenceManager (Scoped)                          │
│    └── 注册 HttpClient.AcceptLanguage ← DefaultThreadCurrentCulture   │
│        ⚠️ 此时 DefaultThreadCurrentCulture = null                    │
│                                                                      │
│  host = builder.Build()  ← 第一次 Build                               │
│    └── host.Services → ClientPreferenceManager                        │
│        └── GetPreference() → localStorage["clientPreference"]         │
│            └── 设置 DefaultThreadCurrentCulture ✅                    │
│                ⚠️ 但 HttpClient 的 Accept-Language 已固化，不会更新    │
│                                                                      │
│  builder.Build().RunAsync()  ← 第二次 Build ⚠️                       │
│    └── 全新的 DI 容器，之前的 host 未被 Dispose                        │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘

┌─────────────────────────── 服务端请求时序 ───────────────────────────┐
│                                                                      │
│  RequestCultureMiddleware.InvokeAsync()                               │
│    ├── ?culture=xxx 查询串 → 设置 CultureInfo (最高优先)              │
│    └── Accept-Language 请求头 → 设置 CultureInfo (降级)               │
│        ⚠️ 因客户端 Accept-Language 未正确设置，此路径失效             │
│                                                                      │
│  PreferencesController.ChangeLanguageAsync()                          │
│    ├── [Authorize(Policy = "Permissions.Preferences.ChangeLanguage")] │
│    └── ServerPreferenceManager.ChangeLanguageAsync()                  │
│        └── ServerStorageService.SetItemAsync()                        │
│            └── ServerStorageProvider._storage (Scoped 内存字典)        │
│                ⚠️ 请求结束后数据丢失                                  │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘

┌─────────────────────────── 服务端配置时序 ───────────────────────────┐
│                                                                      │
│  Startup.ConfigureServices()                                          │
│    ├── RegisterSwagger()                                              │
│    │   └── AddSwaggerGen → GetRegisteredServerLocalizerAsync()        │
│    │       └── BuildServiceProvider() → SetCultureFromServerPreferenceAsync()
│    │           └── ServerPreferenceManager.GetPreference()            │
│    │               └── ServerStorageProvider._storage (空字典)         │
│    │                   ⚠️ 始终返回默认语言 "en-US"                    │
│    │                                                                 │
│    └── AddJwtAuthentication()                                         │
│        └── AddJwtBearer → GetRegisteredServerLocalizerAsync()         │
│            └── 同上，Localizer 始终基于默认文化                       │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 12. 关键文件索引

| 文件 | 作用 |
|------|------|
| [Program.cs (Client)](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Program.cs) | 客户端入口，读取偏好并设置文化 |
| [WebAssemblyHostBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Extensions/WebAssemblyHostBuilderExtensions.cs) | 注册客户端服务，配置 HttpClient Accept-Language |
| [ClientPreferenceManager.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client.Infrastructure/Managers/Preferences/ClientPreferenceManager.cs) | 客户端偏好管理，读写 localStorage |
| [ClientPreference.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client.Infrastructure/Settings/ClientPreference.cs) | 客户端偏好数据模型 |
| [ServerPreferenceManager.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Managers/Preferences/ServerPreferenceManager.cs) | 服务端偏好管理，读写 ServerStorageService |
| [ServerPreference.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Settings/ServerPreference.cs) | 服务端偏好数据模型 |
| [PreferencesController.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/PreferencesController.cs) | 语言切换 API 端点，需权限声明 |
| [RequestCultureMiddleware.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Middlewares/RequestCultureMiddleware.cs) | 请求级文化设置中间件 |
| [ServiceCollectionExtensions.cs (Server)](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs) | 服务配置期文化设置与 Localizer 构造 |
| [ApplicationBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ApplicationBuilderExtensions.cs) | 注册请求本地化与 RequestCultureMiddleware |
| [ServerStorageProvider.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Storage/Provider/ServerStorageProvider.cs) | 服务端存储提供者（内存字典 + NotImplemented） |
| [ServerStorageService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Storage/ServerStorageService.cs) | 服务端存储服务，封装序列化与事件 |
| [ServiceCollectionExtensions.cs (Infrastructure)](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Extensions/ServiceCollectionExtensions.cs) | AddServerStorage 注册 Scoped ServerStorageProvider |
| [Startup.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Startup.cs) | 服务端启动配置 |
| [Permissions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs) | 权限常量定义 |
| [LocalizationConstants.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Localization/LocalizationConstants.cs) | 支持的语言列表 |
| [StorageConstants.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Storage/StorageConstants.cs) | 存储键名常量 |
| [IPreference.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Settings/IPreference.cs) | 偏好接口定义 |
| [IStorageProvider.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Interfaces/Services/Storage/Provider/IServerStorageProvider.cs) | 存储提供者接口定义 |
| [IServerStorageService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Interfaces/Services/Storage/IServerStorageService.cs) | 服务端存储服务接口 |
