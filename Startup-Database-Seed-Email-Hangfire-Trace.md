# 应用启动、数据库、种子数据、后台邮件与 Hangfire 面板全链路追踪

## 1. 总体启动时序

```
Program.Main
  │
  ├─ CreateHostBuilder(args).Build()
  │    └─ Host.CreateDefaultBuilder → UseSerilog → UseStartup<Startup>
  │         ├─ Startup.ConfigureServices  (注册全部服务)
  │         └─ Startup.Configure          (构建中间件管道)
  │
  ├─ context.Database.EnsureCreated()     (创建 SQLite 数据库文件)
  │
  ├─ app.Initialize(_configuration)       (执行种子数据)
  │
  └─ host.RunAsync()                      (启动 Kestrel)
```

---

## 2. Program.Main → CreateHostBuilder → EnsureCreated

### 2.1 入口

[Program.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Program.cs#L15-L39)

```csharp
public async static Task Main(string[] args)
{
    var host = CreateHostBuilder(args).Build();

    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<BlazorHeroContext>();
            context.Database.EnsureCreated();   // ← 关键：确保数据库存在
        }
        catch (Exception ex) { ... }
    }

    await host.RunAsync();
}
```

- `CreateHostBuilder` 调用 `Host.CreateDefaultBuilder`，通过 `UseStartup<Startup>()` 把 `Startup` 类注入管道。
- **在 `Build()` 之后、`RunAsync()` 之前**，显式调用 `EnsureCreated()`。
- `EnsureCreated()` 是 EF Core 的 API，若数据库不存在则根据 `BlazorHeroContext` 的 `OnModelCreating` 创建所有表；若已存在则跳过。
- 注意：`EnsureCreated()` **不会**执行迁移（Migrations），与 `Migrate()` 互斥。

### 2.2 数据库目录如何被 EnsureSqliteDatabaseDirectory 创建

[ServiceCollectionExtensions.cs (Server)](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L241-L250)

```csharp
private static void EnsureSqliteDatabaseDirectory(string connectionString)
{
    var databasePath = GetSqliteDatabasePath(connectionString);
    var databaseDirectory = Path.GetDirectoryName(databasePath);
    if (!string.IsNullOrWhiteSpace(databaseDirectory))
    {
        Directory.CreateDirectory(databaseDirectory);
    }
}
```

- 连接字符串来自 [appsettings.json](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/appsettings.json#L17-L19)：
  ```json
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=Data/blazorhero.db"
  }
  ```
- `GetSqliteDatabasePath` 使用 `SqliteConnectionStringBuilder` 解析出 `DataSource` = `Data/blazorhero.db`。
- `Path.GetDirectoryName` 提取 `Data/` 目录，然后 `Directory.CreateDirectory` 保证该目录存在。
- **该方法在两处被调用**：
  1. `AddDatabase`（[L219](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L218-L224)）— 为应用数据库确保目录。
  2. `AddSqliteHangfireStorage`（[L231](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L226-L233)）— 为 Hangfire 的 SQLite 存储确保目录。
- 这意味着：**应用数据库和 Hangfire 作业存储共用同一连接字符串和同一 SQLite 文件**，目录在 `ConfigureServices` 阶段就已创建，早于 `EnsureCreated()`。

---

## 3. Startup.ConfigureServices 服务注册链

[Startup.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Startup.cs#L32-L68)

```
ConfigureServices 依次调用：
  1. AddForwarding              → SSL 代理 / CORS
  2. AddLocalization            → 本地化
  3. AddCurrentUserService      → HttpContextAccessor + CurrentUserService
  4. AddSerialization           → JSON 序列化器
  5. AddDatabase                → DbContext + DatabaseSeeder 注册
  6. AddServerStorage           → 服务器端存储
  7. AddServerLocalization      → IStringLocalizer 替换实现
  8. AddIdentity                → Identity<BlazorHeroUser, BlazorHeroRole>
  9. AddJwtAuthentication       → JWT Bearer + 全部 Permission 策略
 10. AddSignalR                 → 实时通信
 11. AddApplicationLayer        → MediatR 等
 12. AddApplicationServices     → UserService / RoleService / TokenService 等
 13. AddRepositories            → 仓储注册
 14. AddExtendedAttributesUnitOfWork
 15. AddSharedInfrastructure    → MailConfiguration + SMTPMailService
 16. RegisterSwagger            → Swagger UI
 17. AddInfrastructureMappings  → AutoMapper Profiles
 18. AddHangfire                → SQLite 存储 + 后台作业服务器
 19. AddHangfireServer          → 处理后台作业的 Worker
 20. AddControllers + Validators
 21. AddRazorPages / AddApiVersioning / AddLazyCache
```

### 3.1 AddDatabase

[ServiceCollectionExtensions.cs (Server) L214-L224](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L214-L224)

```csharp
internal static IServiceCollection AddDatabase(
    this IServiceCollection services, IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    EnsureSqliteDatabaseDirectory(connectionString);
    services.AddDbContext<BlazorHeroContext>(options => options.UseSqlite(connectionString));
    return services.AddTransient<IDatabaseSeeder, DatabaseSeeder>();
}
```

- 先确保 `Data/` 目录存在。
- 注册 `BlazorHeroContext`，使用 SQLite。
- 注册 `DatabaseSeeder` 为 `IDatabaseSeeder` 的瞬态实现。

### 3.2 AddIdentity

[ServiceCollectionExtensions.cs (Server) L259-L275](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L259-L275)

```csharp
services.AddIdentity<BlazorHeroUser, BlazorHeroRole>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<BlazorHeroContext>()
.AddDefaultTokenProviders();
```

- 密码策略极宽松：仅要求 6 位长度，不要求大小写/数字/特殊字符。
- Identity 表存储在 `BlazorHeroContext`（SQLite）中。

### 3.3 AddJwtAuthentication

[ServiceCollectionExtensions.cs (Server) L299-L400](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L299-L400)

- 从 `AppConfiguration.Secret` 读取密钥，转为 UTF8 字节作为 `SymmetricSecurityKey`。
- 配置 `JwtBearer` 事件（Token 过期、未授权、禁止访问的响应格式）。
- **关键：自动注册所有 Permission 策略**：

```csharp
foreach (var prop in typeof(Permissions).GetNestedTypes()
    .SelectMany(c => c.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)))
{
    var propertyValue = prop.GetValue(null);
    if (propertyValue is not null)
    {
        options.AddPolicy(propertyValue.ToString(),
            policy => policy.RequireClaim(ApplicationClaimTypes.Permission, propertyValue.ToString()));
    }
}
```

- 通过反射遍历 `Permissions` 类的所有嵌套类的所有静态字段，为每个权限字符串注册一条 `AuthorizationPolicy`。
- 策略要求用户拥有 `ClaimType = "Permission"`、`ClaimValue = 权限字符串` 的声明。

### 3.4 AddHangfire / AddHangfireServer

[ServiceCollectionExtensions.cs (Server) L226-L233](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L226-L233)

```csharp
services.AddHangfire(x => x.AddSqliteHangfireStorage(_configuration));
services.AddHangfireServer();
```

- `AddSqliteHangfireStorage`：同样读取 `DefaultConnection`，确保 `Data/` 目录存在，使用 SQLite 作为 Hangfire 持久化存储。
- `AddHangfireServer`：启动后台作业处理服务器（Worker），在应用进程内运行。

### 3.5 AddSharedInfrastructure

[ServiceCollectionExtensions.cs (Server) L277-L283](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L277-L283)

```csharp
internal static IServiceCollection AddSharedInfrastructure(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddTransient<IDateTimeService, SystemDateTimeService>();
    services.Configure<MailConfiguration>(configuration.GetSection("MailConfiguration"));
    services.AddTransient<IMailService, SMTPMailService>();
    return services;
}
```

- 将 `appsettings.json` 中的 `MailConfiguration` 节绑定到 [MailConfiguration](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Configurations/MailConfiguration.cs) 配置类。
- 注册 `SMTPMailService` 为 `IMailService` 的实现。

---

## 4. Startup.Configure → Initialize → 种子数据

### 4.1 Configure 管道

[Startup.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Startup.cs#L70-L95)

```
UseForwarding → UseExceptionHandling → UseHttpsRedirection
→ UseMiddleware<ErrorHandlerMiddleware> → UseBlazorFrameworkFiles
→ UseStaticFiles → UseRequestLocalizationByCulture → UseRouting
→ UseAuthentication → UseAuthorization
→ UseHangfireDashboard("/jobs", ...)
→ UseEndpoints → ConfigureSwagger → Initialize
```

- `UseHangfireDashboard("/jobs", ...)` 挂载 Hangfire 面板到 `/jobs` 路径。
- `app.Initialize(_configuration)` 是最后一步，执行种子数据。

### 4.2 ApplicationBuilderExtensions.Initialize

[ApplicationBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ApplicationBuilderExtensions.cs#L81-L93)

```csharp
internal static IApplicationBuilder Initialize(
    this IApplicationBuilder app, IConfiguration _configuration)
{
    using var serviceScope = app.ApplicationServices.CreateScope();
    var initializers = serviceScope.ServiceProvider.GetServices<IDatabaseSeeder>();
    foreach (var initializer in initializers)
    {
        initializer.Initialize();
    }
    return app;
}
```

- 从 DI 容器解析所有 `IDatabaseSeeder` 实现（目前只有 `DatabaseSeeder`）。
- 调用 `Initialize()` 写入种子数据。

### 4.3 DatabaseSeeder.Initialize → AddAdministrator → AddBasicUser

[DatabaseSeeder.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/DatabaseSeeder.cs#L40-L128)

```csharp
public void Initialize()
{
    AddAdministrator();
    AddBasicUser();
    _db.SaveChanges();
}
```

#### AddAdministrator 流程

1. 查找 `Administrator` 角色，不存在则创建。
2. 创建超级用户 `mukesh@blazorhero.com`（UserName: `mukesh`），不存在则用 `UserConstants.DefaultPassword` 创建并分配 `Administrator` 角色。
3. **为 Administrator 角色添加全部权限声明**：

```csharp
foreach (var permission in Permissions.GetRegisteredPermissions())
{
    await _roleManager.AddPermissionClaim(adminRoleInDb, permission);
}
```

- `Permissions.GetRegisteredPermissions()` 通过反射遍历 [Permissions](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs) 类的所有嵌套类（Products、Brands、Documents、DocumentTypes、DocumentExtendedAttributes、Users、Roles、RoleClaims、Communication、Preferences、Dashboards、Hangfire、AuditTrails）的所有 `const string` 字段，收集为列表。
- `AddPermissionClaim`（[ClaimsHelper](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Helpers/ClaimExtensions.cs#L46-L55)）检查角色是否已有该 Claim，没有则添加 `Claim(ApplicationClaimTypes.Permission, permission)`，即 `Claim("Permission", "Permissions.XXX.YYY")`。

#### AddBasicUser 流程

1. 查找 `Basic` 角色，不存在则创建。
2. 创建基础用户 `john@blazorhero.com`（UserName: `johndoe`），不存在则用 `UserConstants.DefaultPassword` 创建并分配 `Basic` 角色。
3. **Basic 角色不自动获得任何权限声明**（无 `AddPermissionClaim` 调用）。

### 4.4 默认账号一览

| 用户 | 邮箱 | 用户名 | 角色 | 密码 |
|------|------|--------|------|------|
| 超级管理员 | mukesh@blazorhero.com | mukesh | Administrator | `123Pa$$word!` |
| 基础用户 | john@blazorhero.com | johndoe | Basic | `123Pa$$word!` |

密码来源：
- [UserConstants.DefaultPassword](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/User/UserConstants.cs#L5) = `"123Pa$$word!"`
- [RoleConstants.DefaultPassword](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Role/RoleConstants.cs#L7) = `"123Pa$$word!"`（重复定义，DatabaseSeeder 使用 UserConstants）

---

## 5. 注册与忘记密码 → BackgroundJob.Enqueue → SMTPMailService

### 5.1 UserService.RegisterAsync

[UserService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/UserService.cs#L63-L121)

```
RegisterAsync(request, origin)
  ├─ 检查用户名/邮箱是否已存在
  ├─ _userManager.CreateAsync(user, request.Password)
  ├─ _userManager.AddToRoleAsync(user, "Basic")
  ├─ 若 !request.AutoConfirmEmail:
  │     ├─ SendVerificationEmail(user, origin) → 生成确认链接
  │     ├─ 构造 MailRequest
  │     └─ BackgroundJob.Enqueue(() => _mailService.SendAsync(mailRequest))  ← 入队
  └─ 返回结果
```

- **仅当 `AutoConfirmEmail = false` 时**，才会发送确认邮件。
- 使用 Hangfire 的 `BackgroundJob.Enqueue`，邮件发送在后台异步执行，不阻塞请求。

### 5.2 UserService.ForgotPasswordAsync

[UserService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/UserService.cs#L227-L250)

```
ForgotPasswordAsync(request, origin)
  ├─ 查找用户并验证邮箱已确认
  ├─ _userManager.GeneratePasswordResetTokenAsync(user)
  ├─ Base64UrlEncode(code)
  ├─ 构造重置密码链接 + MailRequest
  └─ BackgroundJob.Enqueue(() => _mailService.SendAsync(mailRequest))  ← 入队
```

- **始终**通过 `BackgroundJob.Enqueue` 发送重置密码邮件。
- 即使用户不存在或邮箱未确认，也不暴露信息（返回统一错误消息），但不会入队邮件。

### 5.3 SMTPMailService.SendAsync

[SMTPMailService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure.Shared/Services/SMTPMailService.cs#L24-L48)

```csharp
public async Task SendAsync(MailRequest request)
{
    var email = new MimeMessage { ... };
    email.To.Add(MailboxAddress.Parse(request.To));
    using var smtp = new SmtpClient();
    await smtp.ConnectAsync(_config.Host, _config.Port, SecureSocketOptions.StartTls);
    await smtp.AuthenticateAsync(_config.UserName, _config.Password);
    await smtp.SendAsync(email);
    await smtp.DisconnectAsync(true);
}
```

- 使用 MailKit 库通过 SMTP 发送邮件。
- 配置来自 `IOptions<MailConfiguration>`，即 `appsettings.json` 的 `MailConfiguration` 节。

### 5.4 SMTP 配置来源

[appsettings.json](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/appsettings.json#L20-L27)

```json
"MailConfiguration": {
    "From": "info@codewithmukesh.com",
    "Host": "smtp.ethereal.email",
    "Port": 587,
    "UserName": "adaline.pfannerstill49@ethereal.email",
    "Password": "vAKmWQB8CyPUBg8rBQ",
    "DisplayName": "Mukesh Murugan"
}
```

- 绑定流程：`AddSharedInfrastructure` → `services.Configure<MailConfiguration>(configuration.GetSection("MailConfiguration"))` → `SMTPMailService` 构造函数注入 `IOptions<MailConfiguration>`。
- 当前使用 [Ethereal Email](https://ethereal.email)（开发用假 SMTP 服务），生产环境需替换。
- **SMTP 密码明文存储在 appsettings.json 中**。

---

## 6. Hangfire 面板与授权

### 6.1 面板挂载

[Startup.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Startup.cs#L87-L91)

```csharp
app.UseHangfireDashboard("/jobs", new DashboardOptions
{
    DashboardTitle = localizer["BlazorHero Jobs"],
    Authorization = new[] { new HangfireAuthorizationFilter() }
});
```

- 面板路径：`/jobs`
- 授权过滤器：`HangfireAuthorizationFilter`

### 6.2 HangfireAuthorizationFilter.Authorize

[HangfireAuthorizationFilter.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Filters/HangfireAuthorizationFilter.cs#L5-L19)

```csharp
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        //TODO implement authorization logic
        //var httpContext = context.GetHttpContext();
        //return httpContext.User.Identity.IsAuthenticated;
        //return httpContext.User.IsInRole(Permissions.Hangfire.View);
        return true;   // ← 对所有请求返回 true
    }
}
```

- **当前实现直接返回 `true`**，意味着任何匿名用户都可以访问 `/jobs` 面板。
- 注释中已有两个备选方案：
  1. `httpContext.User.Identity.IsAuthenticated` — 仅认证用户可访问。
  2. `httpContext.User.IsInRole(Permissions.Hangfire.View)` — 仅拥有 Hangfire 查看权限的用户可访问。
- 但两个方案都被注释掉了，TODO 未完成。

### 6.3 为什么 `/jobs` 面板对所有请求返回 true

1. `HangfireAuthorizationFilter` 实现了 `IDashboardAuthorizationFilter` 接口。
2. `Authorize` 方法是 Hangfire 调用授权检查的入口。
3. 方法体中唯一的执行路径是 `return true;`。
4. 其余代码全部被注释，且没有其他 `Authorize` 实现被注册。
5. 因此，**任何 HTTP 请求（包括未认证的请求）都能访问 Hangfire 仪表板**，可以查看、删除、重试后台作业。

---

## 7. 安全影响分析

### 7.1 默认账号密码

| 风险项 | 详情 |
|--------|------|
| 管理员账号 | `mukesh` / `123Pa$$word!` — 硬编码在源码中，任何人都可以从源码获取 |
| 基础用户账号 | `johndoe` / `123Pa$$word!` — 同上 |
| 密码常量 | `UserConstants.DefaultPassword` 和 `RoleConstants.DefaultPassword` 重复定义，均为 `123Pa$$word!` |
| 种子数据未检查 | `AddAdministrator` 中 `AddPermissionClaim` 在每次启动时都会执行，即使角色已存在，仍会尝试添加（被 `AddPermissionClaim` 内部的去重逻辑阻止），但逻辑上不够优雅 |

**部署影响**：若不修改默认密码直接部署到生产环境，攻击者可用公开的账号密码登录管理员账户，获得全部权限。

### 7.2 固定 JWT Secret

[appsettings.json](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/appsettings.json#L12-L13)

```json
"AppConfiguration": {
    "Secret": "S0M3RAN0MS3CR3T!1!MAG1C!1!",
    ...
}
```

- JWT 签名密钥硬编码为 `S0M3RAN0MS3CR3T!1!MAG1C!1!`。
- [AddJwtAuthentication](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L301-L302) 中：
  ```csharp
  var key = Encoding.UTF8.GetBytes(config.Secret);
  ```
- 任何获得此密钥的人都可以伪造有效的 JWT Token，冒充任意用户（包括 Administrator）。
- **密钥长度仅 27 字节（216 位）**，低于推荐的 256 位（HMAC-SHA256）。

### 7.3 Hangfire 面板无认证

- `/jobs` 端点完全开放，匿名用户可查看所有后台作业详情（包括邮件收件人地址等敏感信息）。
- 可手动触发、删除、重试作业，造成业务逻辑混乱或信息泄露。

### 7.4 SMTP 凭证明文存储

- `appsettings.json` 中 SMTP 用户名/密码明文存储。
- 若代码仓库公开（如 MIT 许可证项目），凭证将暴露。

### 7.5 宽松密码策略

- Identity 密码策略仅要求 6 位长度，不要求大小写、数字、特殊字符，降低了暴力破解难度。

---

## 8. 回归测试思路

### 8.1 数据库与种子数据测试

| 测试用例 | 验证要点 |
|----------|----------|
| 空数据库首次启动 | `EnsureCreated` 创建所有表；`Data/` 目录自动创建；`Administrator` 和 `Basic` 角色存在；两个默认用户存在 |
| 重复启动（数据库已存在） | `EnsureCreated` 不报错；种子数据不重复插入（角色/用户已存在时跳过）；权限 Claim 不重复添加 |
| Administrator 角色权限完整性 | Administrator 角色的 Claim 数量 = `Permissions.GetRegisteredPermissions().Count`；每个 Claim 的 Type 为 `"Permission"` |
| Basic 角色无权限声明 | Basic 角色的 Claim 列表为空 |
| 默认用户密码验证 | `userManager.CheckPasswordAsync(adminUser, "123Pa$$word!")` 返回 true |
| 连接字符串目录创建 | 使用 `Data/SubDir/blazorhero.db` 形式连接字符串时，`Data/SubDir/` 目录被自动创建 |

### 8.2 邮件与后台作业测试

| 测试用例 | 验证要点 |
|----------|----------|
| 注册新用户（AutoConfirmEmail=false） | `BackgroundJob.Enqueue` 被调用一次；`MailRequest.To` 等于新用户邮箱；`MailRequest.Subject` 包含 "Confirm" |
| 注册新用户（AutoConfirmEmail=true） | 不调用 `BackgroundJob.Enqueue`；不发送邮件 |
| 忘记密码（有效邮箱） | `BackgroundJob.Enqueue` 被调用一次；邮件包含重置密码链接 |
| 忘记密码（无效邮箱） | 不调用 `BackgroundJob.Enqueue`；返回统一错误消息 |
| SMTPMailService.SendAsync 异常 | 异常被 catch 记录日志，不向上抛出；Hangfire 将作业标记为 Failed |
| SMTP 配置绑定 | `IOptions<MailConfiguration>.Value.Host` 等于 `appsettings.json` 中 `MailConfiguration.Host` |

### 8.3 Hangfire 面板与授权测试

| 测试用例 | 验证要点 |
|----------|----------|
| 匿名访问 `/jobs` | 返回 HTTP 200（当前行为）；修复后应返回 401/403 |
| Hangfire 仪表板标题 | 页面包含 "BlazorHero Jobs" |
| Hangfire 存储使用 SQLite | `Hangfire.Schema` 表存在于同一 `blazorhero.db` 文件中 |
| `HangfireAuthorizationFilter.Authorize` 返回值 | 当前为 `true`；修复后应检查 `httpContext.User.Identity.IsAuthenticated` |

### 8.4 JWT 认证与权限测试

| 测试用例 | 验证要点 |
|----------|----------|
| 使用默认 Secret 签发的 Token | 能通过 `AddJwtAuthentication` 的验证 |
| Token 中包含 Permission Claim | Administrator 用户的 Token 包含所有 `Permissions.XXX.YYY` Claim |
| 无权限用户访问受保护 API | 返回 403 Forbidden |
| Token 过期 | 返回 401 + "The Token is expired." |
| 无 Token 访问受保护 API | 返回 401 + "You are not Authorized." |

### 8.5 安全回归测试

| 测试用例 | 验证要点 |
|----------|----------|
| 默认密码未修改时部署检查 | CI/CD 管道检测 `DefaultPassword` 是否仍为 `"123Pa$$word!"`，是则失败 |
| JWT Secret 强度检查 | Secret 长度 >= 32 字节；不等于默认值 |
| Hangfire 面板认证 | 非管理员访问 `/jobs` 返回 403 |
| SMTP 密码不在源码中 | `appsettings.json` 不包含明文密码；使用 User Secrets / 环境变量 / Key Vault |
| 密码策略强度 | `RegisterAsync` 中弱密码被拒绝（若策略收紧后） |

---

## 9. 完整调用链图

```
┌──────────────────────────────────────────────────────────────────┐
│ Program.Main                                                     │
│   ├─ CreateHostBuilder(args).Build()                             │
│   │    ├─ Startup.ConfigureServices()                            │
│   │    │    ├─ AddDatabase()                                     │
│   │    │    │    ├─ EnsureSqliteDatabaseDirectory("Data/blazor…")│
│   │    │    │    ├─ AddDbContext<BlazorHeroContext>(SQLite)       │
│   │    │    │    └─ AddTransient<IDatabaseSeeder, DatabaseSeeder>│
│   │    │    ├─ AddIdentity() ← 宽松密码策略                      │
│   │    │    ├─ AddJwtAuthentication()                            │
│   │    │    │    ├─ Secret → SymmetricSecurityKey                │
│   │    │    │    └─ 反射注册全部 Permission Policy               │
│   │    │    ├─ AddSharedInfrastructure()                         │
│   │    │    │    ├─ Configure<MailConfiguration>(appsettings)    │
│   │    │    │    └─ AddTransient<IMailService, SMTPMailService>  │
│   │    │    ├─ AddHangfire(AddSqliteHangfireStorage)             │
│   │    │    │    └─ EnsureSqliteDatabaseDirectory (再次)         │
│   │    │    └─ AddHangfireServer()                               │
│   │    └─ Startup.Configure()                                    │
│   │         ├─ UseHangfireDashboard("/jobs", HangfireAuthFilter) │
│   │         └─ app.Initialize()                                  │
│   │              └─ DatabaseSeeder.Initialize()                  │
│   │                   ├─ AddAdministrator()                      │
│   │                   │    ├─ Create Role "Administrator"        │
│   │                   │    ├─ Create User mukesh (123Pa$$word!)  │
│   │                   │    └─ AddPermissionClaim × N (全部权限)   │
│   │                   └─ AddBasicUser()                          │
│   │                        ├─ Create Role "Basic"                │
│   │                        └─ Create User johndoe (123Pa$$word!) │
│   ├─ context.Database.EnsureCreated()                            │
│   └─ host.RunAsync()                                             │
└──────────────────────────────────────────────────────────────────┘

运行时：
┌──────────────────────────────┐
│ UserService.RegisterAsync()  │──→ BackgroundJob.Enqueue() ──→ SMTPMailService.SendAsync()
│ UserService.ForgotPassword() │──→ BackgroundJob.Enqueue() ──→ SMTPMailService.SendAsync()
└──────────────────────────────┘         │
                                         ▼
                                   Hangfire Server (进程内 Worker)
                                   读取 SQLite 作业队列 → 执行 SendAsync
                                   MailKit → SMTP (smtp.ethereal.email:587)

/jobs 面板：
  HangfireAuthorizationFilter.Authorize() → return true (无认证)
```

---

## 10. 关键源码索引

| 组件 | 文件 |
|------|------|
| 应用入口 | [Program.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Program.cs) |
| 服务注册与中间件 | [Startup.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Startup.cs) |
| AddDatabase / AddIdentity / AddJwtAuthentication / AddHangfire / AddSharedInfrastructure / EnsureSqliteDatabaseDirectory | [ServiceCollectionExtensions.cs (Server)](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs) |
| Initialize (种子数据入口) | [ApplicationBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ApplicationBuilderExtensions.cs) |
| 种子数据实现 | [DatabaseSeeder.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/DatabaseSeeder.cs) |
| 权限定义与反射收集 | [Permissions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs) |
| AddPermissionClaim 扩展 | [ClaimExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Helpers/ClaimExtensions.cs) |
| 用户注册/忘记密码/后台邮件入队 | [UserService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/UserService.cs) |
| SMTP 邮件发送 | [SMTPMailService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure.Shared/Services/SMTPMailService.cs) |
| Hangfire 授权过滤器 | [HangfireAuthorizationFilter.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Filters/HangfireAuthorizationFilter.cs) |
| 配置文件 | [appsettings.json](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/appsettings.json) |
| 邮件配置类 | [MailConfiguration.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Configurations/MailConfiguration.cs) |
| 应用配置类 | [AppConfiguration.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Configurations/AppConfiguration.cs) |
| 默认密码 | [UserConstants.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/User/UserConstants.cs) |
| 角色常量 | [RoleConstants.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Role/RoleConstants.cs) |
| Claim 类型常量 | [ApplicationClaimType.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/ApplicationClaimType.cs) |
| EF 上下文 | [BlazorHeroContext.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/BlazorHeroContext.cs) |
