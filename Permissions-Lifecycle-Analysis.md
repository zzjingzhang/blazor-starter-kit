# 权限系统完整生命周期分析 - Permissions.Products.Archive 实现指南

## 一、权限常量定义

### 1.1 新增权限常量

首先需要在 [Permissions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs#L12-L20) 的 `Products` 静态类中添加 `Archive` 权限常量：

```csharp
public static class Products
{
    public const string View = "Permissions.Products.View";
    public const string Create = "Permissions.Products.Create";
    public const string Edit = "Permissions.Products.Edit";
    public const string Delete = "Permissions.Products.Delete";
    public const string Export = "Permissions.Products.Export";
    public const string Search = "Permissions.Products.Search";
    public const string Archive = "Permissions.Products.Archive";  // 新增
}
```

### 1.2 权限常量的反射机制

[Permissions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs#L147-L157) 中的 `GetRegisteredPermissions()` 方法通过反射自动发现所有权限常量：

```csharp
public static List<string> GetRegisteredPermissions()
{
    var permissions = new List<string>();
    foreach (var prop in typeof(Permissions).GetNestedTypes()
        .SelectMany(c => c.GetFields(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)))
    {
        var propertyValue = prop.GetValue(null);
        if (propertyValue is not null)
            permissions.Add(propertyValue.ToString());
    }
    return permissions;
}
```

---

## 二、服务端授权策略注册 - AddJwtAuthentication

### 2.1 动态注册Policy

在 [ServiceCollectionExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L387-L398) 的 `AddJwtAuthentication` 方法中，通过反射自动将所有权限常量注册为授权策略：

```csharp
services.AddAuthorization(options =>
{
    foreach (var prop in typeof(Permissions).GetNestedTypes()
        .SelectMany(c => c.GetFields(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)))
    {
        var propertyValue = prop.GetValue(null);
        if (propertyValue is not null)
        {
            options.AddPolicy(
                propertyValue.ToString(),
                policy => policy.RequireClaim(
                    ApplicationClaimTypes.Permission,
                    propertyValue.ToString()));
        }
    }
});
```

**关键说明**：
- 利用反射遍历 `Permissions` 类下所有嵌套类的所有公共静态字段
- 每个权限常量（如 `Permissions.Products.Archive`）会被注册为一个独立的 Policy
- Policy 要求用户的 Claims 中必须包含 `ApplicationClaimTypes.Permission` 类型且值等于该权限常量
- 新增的 `Archive` 权限**无需手动注册**，会被自动发现并注册

### 2.2 JWT认证配置

在 [ServiceCollectionExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L299-L386) 中配置的JWT认证参数：

```csharp
bearer.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(key),
    ValidateIssuer = false,
    ValidateAudience = false,
    RoleClaimType = ClaimTypes.Role,
    ClockSkew = TimeSpan.Zero
};
```

---

## 三、客户端授权策略注册 - RegisterPermissionClaims

### 3.1 客户端Policy注册

在 [WebAssemblyHostBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Extensions/WebAssemblyHostBuilderExtensions.cs#L111-L121) 中，`RegisterPermissionClaims` 方法同样通过反射注册客户端本地授权策略：

```csharp
private static void RegisterPermissionClaims(AuthorizationOptions options)
{
    foreach (var prop in typeof(Permissions).GetNestedTypes()
        .SelectMany(c => c.GetFields(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)))
    {
        var propertyValue = prop.GetValue(null);
        if (propertyValue is not null)
        {
            options.AddPolicy(
                propertyValue.ToString(),
                policy => policy.RequireClaim(
                    ApplicationClaimTypes.Permission,
                    propertyValue.ToString()));
        }
    }
}
```

该方法在 `AddClientServices` 中被调用 [第45行](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Extensions/WebAssemblyHostBuilderExtensions.cs#L43-L46)：

```csharp
.AddAuthorizationCore(options =>
{
    RegisterPermissionClaims(options);
})
```

**关键说明**：
- 客户端与服务端使用**完全相同**的反射逻辑注册Policy
- 确保客户端本地授权检查与服务端保持一致
- 新增权限同样无需修改客户端代码，自动生效

---

## 四、权限展示 - GetAllPermissionsAsync / GetAllPermissions

### 4.1 ClaimsHelper.GetAllPermissions 扩展方法

在 [ClaimExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Helpers/ClaimExtensions.cs#L16-L44) 中定义的扩展方法，用于获取所有权限并生成待选项：

```csharp
public static void GetAllPermissions(this List<RoleClaimResponse> allPermissions)
{
    var modules = typeof(Permissions).GetNestedTypes();

    foreach (var module in modules)
    {
        var moduleName = string.Empty;
        var moduleDescription = string.Empty;

        if (module.GetCustomAttributes(typeof(DisplayNameAttribute), true)
            .FirstOrDefault() is DisplayNameAttribute displayNameAttribute)
            moduleName = displayNameAttribute.DisplayName;

        if (module.GetCustomAttributes(typeof(DescriptionAttribute), true)
            .FirstOrDefault() is DescriptionAttribute descriptionAttribute)
            moduleDescription = descriptionAttribute.Description;

        var fields = module.GetFields(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        foreach (var fi in fields)
        {
            var propertyValue = fi.GetValue(null);
            if (propertyValue is not null)
                allPermissions.Add(new RoleClaimResponse
                {
                    Value = propertyValue.ToString(),
                    Type = ApplicationClaimTypes.Permission,
                    Group = moduleName,
                    Description = moduleDescription
                });
        }
    }
}
```

**关键说明**：
- 通过反射遍历 `Permissions` 的所有嵌套类（即模块，如Products、Brands等）
- 读取 `[DisplayName]` 和 `[Description]` 特性作为分组信息
- 将每个权限常量转换为 `RoleClaimResponse` 对象，包含：
  - `Value`: 权限值（如 "Permissions.Products.Archive"）
  - `Type`: Claim类型（固定为 `ApplicationClaimTypes.Permission`）
  - `Group`: 分组名（如 "Products"）
  - `Description`: 模块描述
- **新增的 `Archive` 权限会自动出现在待选列表中**

### 4.2 RoleService.GetAllPermissionsAsync

在 [RoleService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/RoleService.cs#L83-L124) 中，获取指定角色的所有权限（含已选中状态）：

```csharp
public async Task<Result<PermissionResponse>> GetAllPermissionsAsync(string roleId)
{
    var model = new PermissionResponse();
    var allPermissions = GetAllPermissions(); // 调用私有方法
    var role = await _roleManager.FindByIdAsync(roleId);
    
    if (role != null)
    {
        model.RoleId = role.Id;
        model.RoleName = role.Name;
        var roleClaimsResult = await _roleClaimService.GetAllByRoleIdAsync(role.Id);
        
        if (roleClaimsResult.Succeeded)
        {
            var roleClaims = roleClaimsResult.Data;
            var allClaimValues = allPermissions.Select(a => a.Value).ToList();
            var roleClaimValues = roleClaims.Select(a => a.Value).ToList();
            var authorizedClaims = allClaimValues.Intersect(roleClaimValues).ToList();
            
            foreach (var permission in allPermissions)
            {
                if (authorizedClaims.Any(a => a == permission.Value))
                {
                    permission.Selected = true;
                    // 合并数据库中存储的描述和分组信息
                }
            }
        }
    }
    model.RoleClaims = allPermissions;
    return await Result<PermissionResponse>.SuccessAsync(model);
}

private List<RoleClaimResponse> GetAllPermissions()
{
    var allPermissions = new List<RoleClaimResponse>();
    allPermissions.GetAllPermissions(); // 调用ClaimsHelper扩展方法
    return allPermissions;
}
```

**关键说明**：
- 首先通过 `GetAllPermissions()` 获取所有系统权限（包含新增的Archive）
- 查询该角色已有的权限声明
- 通过 `Intersect` 找出已授权的权限，将对应的 `Selected` 标记为 `true`
- 返回给前端用于渲染权限勾选界面

---

## 五、权限写入角色声明 - UpdatePermissionsAsync / AddPermissionClaim

### 5.1 ClaimsHelper.AddPermissionClaim 扩展方法

在 [ClaimExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Helpers/ClaimExtensions.cs#L46-L56) 中，为角色添加权限声明：

```csharp
public static async Task<IdentityResult> AddPermissionClaim(
    this RoleManager<BlazorHeroRole> roleManager,
    BlazorHeroRole role,
    string permission)
{
    var allClaims = await roleManager.GetClaimsAsync(role);
    if (!allClaims.Any(a => a.Type == ApplicationClaimTypes.Permission 
        && a.Value == permission))
    {
        return await roleManager.AddClaimAsync(role,
            new Claim(ApplicationClaimTypes.Permission, permission));
    }
    return IdentityResult.Failed();
}
```

**关键说明**：
- 先检查该角色是否已有该权限声明，避免重复添加
- 使用 `ApplicationClaimTypes.Permission` 作为Claim类型
- 权限值（如 `Permissions.Products.Archive`）作为Claim的Value

### 5.2 RoleService.UpdatePermissionsAsync

在 [RoleService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/RoleService.cs#L177-L253) 中，更新角色权限：

```csharp
public async Task<Result<string>> UpdatePermissionsAsync(PermissionRequest request)
{
    var errors = new List<string>();
    var role = await _roleManager.FindByIdAsync(request.RoleId);
    
    // 安全检查：防止修改Administrator角色权限
    if (role.Name == RoleConstants.AdministratorRole)
    {
        // 验证当前用户是否为管理员...
    }
    
    // 1. 移除该角色所有现有权限声明
    var claims = await _roleManager.GetClaimsAsync(role);
    foreach (var claim in claims)
    {
        await _roleManager.RemoveClaimAsync(role, claim);
    }
    
    // 2. 添加所有选中的权限声明（包含新增的Archive）
    var selectedClaims = request.RoleClaims.Where(a => a.Selected).ToList();
    foreach (var claim in selectedClaims)
    {
        var addResult = await _roleManager.AddPermissionClaim(role, claim.Value);
        if (!addResult.Succeeded)
        {
            errors.AddRange(addResult.Errors.Select(
                e => _localizer[e.Description].ToString()));
        }
    }
    
    // 3. 保存额外信息到RoleClaim表（描述、分组等）
    var addedClaims = await _roleClaimService.GetAllByRoleIdAsync(role.Id);
    // ... 保存额外信息 ...
    
    return errors.Any() 
        ? await Result<string>.FailAsync(errors) 
        : await Result<string>.SuccessAsync(_localizer["Permissions Updated."]);
}
```

**关键说明**：
- 采用"全量替换"策略：先删除所有，再重新添加选中的
- 如果管理员角色被修改，会确保关键权限不被取消
- 新增的 `Archive` 权限只要被勾选，就会被写入角色声明

---

## 六、角色声明放入JWT - IdentityService.GetClaimsAsync

在 [IdentityService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/IdentityService.cs#L101-L128) 中，生成JWT时将角色权限声明加入Token：

```csharp
private async Task<IEnumerable<Claim>> GetClaimsAsync(BlazorHeroUser user)
{
    var userClaims = await _userManager.GetClaimsAsync(user);
    var roles = await _userManager.GetRolesAsync(user);
    var roleClaims = new List<Claim>();
    var permissionClaims = new List<Claim>();
    
    foreach (var role in roles)
    {
        roleClaims.Add(new Claim(ClaimTypes.Role, role));
        var thisRole = await _roleManager.FindByNameAsync(role);
        // 获取该角色的所有权限声明
        var allPermissionsForThisRoles = await _roleManager.GetClaimsAsync(thisRole);
        permissionClaims.AddRange(allPermissionsForThisRoles);
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Name, user.FirstName),
        new(ClaimTypes.Surname, user.LastName),
        new(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty)
    }
    .Union(userClaims)         // 用户个人声明
    .Union(roleClaims)         // 角色声明
    .Union(permissionClaims);  // 权限声明（包含Archive）

    return claims;
}
```

**关键说明**：
- 遍历用户所属的所有角色
- 对每个角色，通过 `_roleManager.GetClaimsAsync(thisRole)` 获取该角色的所有权限声明
- 将这些权限声明全部加入JWT的Claims中
- JWT最终包含：用户基本信息 + 用户个人声明 + 角色声明 + 所有角色的权限声明
- **如果用户角色拥有 `Archive` 权限，该权限会被包含在JWT中**

该方法被 `GenerateJwtAsync` [第95-98行](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/IdentityService.cs#L95-L98) 和 `GetRefreshTokenAsync` [第87行](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/IdentityService.cs#L87) 调用，确保登录和刷新Token时都包含最新权限。

---

## 七、权限变更后的令牌刷新与登出机制

### 7.1 RolePermissions.razor.cs 发送SignalR消息

在 [RolePermissions.razor.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Pages/Identity/RolePermissions.razor.cs#L94-L112) 的 `SaveAsync` 方法中，权限更新成功后发送两条SignalR消息：

```csharp
private async Task SaveAsync()
{
    var request = _mapper.Map<PermissionResponse, PermissionRequest>(_model);
    var result = await RoleManager.UpdatePermissionsAsync(request);
    if (result.Succeeded)
    {
        _snackBar.Add(result.Messages[0], Severity.Success);
        
        // 消息1：请求所有客户端刷新Token
        await HubConnection.SendAsync(
            ApplicationConstants.SignalR.SendRegenerateTokens);
        
        // 消息2：通知该角色的用户需要登出
        await HubConnection.SendAsync(
            ApplicationConstants.SignalR.OnChangeRolePermissions,
            _currentUser.GetUserId(),
            request.RoleId);
        
        _navigationManager.NavigateTo("/identity/roles");
    }
}
```

### 7.2 SignalRHub 转发消息

在 [SignalRHub.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Hubs/SignalRHub.cs#L31-L55) 中，服务端Hub接收并转发消息：

```csharp
public async Task OnChangeRolePermissions(string userId, string roleId)
{
    await Clients.All.SendAsync(
        ApplicationConstants.SignalR.LogoutUsersByRole,
        userId,
        roleId);
}

public async Task RegenerateTokensAsync()
{
    await Clients.All.SendAsync(
        ApplicationConstants.SignalR.ReceiveRegenerateTokens);
}
```

常量定义在 [ApplicationConstants.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Application/ApplicationConstants.cs#L10-L22)：

```csharp
public const string SendRegenerateTokens = "RegenerateTokensAsync";
public const string ReceiveRegenerateTokens = "RegenerateTokens";
public const string OnChangeRolePermissions = "OnChangeRolePermissions";
public const string LogoutUsersByRole = "LogoutUsersByRole";
```

### 7.3 MainBody.razor.cs 处理消息

在 [MainBody.razor.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Shared/MainBody.razor.cs#L76-L116) 中注册两个SignalR事件处理器：

**处理器1：ReceiveRegenerateTokens - 强制刷新Token**

```csharp
hubConnection.On(ApplicationConstants.SignalR.ReceiveRegenerateTokens,
    async () =>
{
    try
    {
        var token = await _authenticationManager.TryForceRefreshToken();
        if (!string.IsNullOrEmpty(token))
        {
            _snackBar.Add(_localizer["Refreshed Token."], Severity.Success);
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
        _snackBar.Add(_localizer["You are Logged Out."], Severity.Error);
        await _authenticationManager.Logout();
        _navigationManager.NavigateTo("/");
    }
});
```

**处理器2：LogoutUsersByRole - 登出受影响用户**

```csharp
hubConnection.On<string, string>(
    ApplicationConstants.SignalR.LogoutUsersByRole,
    async (userId, roleId) =>
{
    if (CurrentUserId != userId)  // 修改者本人不登出
    {
        var rolesResponse = await RoleManager.GetRolesAsync();
        if (rolesResponse.Succeeded)
        {
            var role = rolesResponse.Data.FirstOrDefault(x => x.Id == roleId);
            if (role != null)
            {
                var currentUserRolesResponse =
                    await _userManager.GetRolesAsync(CurrentUserId);
                if (currentUserRolesResponse.Succeeded
                    && currentUserRolesResponse.Data.UserRoles
                        .Any(x => x.RoleName == role.Name))
                {
                    _snackBar.Add(
                        _localizer["You are logged out because the Permissions of one of your Roles have been updated."],
                        Severity.Error);
                    await hubConnection.SendAsync(
                        ApplicationConstants.SignalR.OnDisconnect,
                        CurrentUserId);
                    await _authenticationManager.Logout();
                    _navigationManager.NavigateTo("/login");
                }
            }
        }
    }
});
```

**关键说明**：
1. **SendRegenerateTokens → ReceiveRegenerateTokens**：
   - 所有在线客户端收到消息后尝试强制刷新Token
   - 刷新成功则使用新Token（包含最新权限）
   - 刷新失败则登出

2. **OnChangeRolePermissions → LogoutUsersByRole**：
   - 检查当前用户是否属于被修改权限的角色
   - 如果是且不是修改者本人，则强制登出
   - 确保权限变更立即生效，避免旧Token继续使用

---

## 八、授权层分析

### 8.1 ProductsController - 服务端API授权

在 [ProductsController.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/v1/Catalog/ProductsController.cs#L13-L79) 中，使用 `[Authorize]` 特性在**Controller层**进行授权：

```csharp
public class ProductsController : BaseApiController<ProductsController>
{
    [Authorize(Policy = Permissions.Products.View)]
    [HttpGet]
    public async Task<IActionResult> GetAll(...) { ... }

    [Authorize(Policy = Permissions.Products.View)]
    [HttpGet("image/{id}")]
    public async Task<IActionResult> GetProductImageAsync(int id) { ... }

    [Authorize(Policy = Permissions.Products.Create)]
    [HttpPost]
    public async Task<IActionResult> Post(AddEditProductCommand command) { ... }

    [Authorize(Policy = Permissions.Products.Delete)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) { ... }

    [Authorize(Policy = Permissions.Products.Export)]
    [HttpGet("export")]
    public async Task<IActionResult> Export(string searchString = "") { ... }
    
    // 新增归档接口
    [Authorize(Policy = Permissions.Products.Archive)]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(int id) { ... }
}
```

**授权层说明**：
- **属于服务端API层授权**
- 基于ASP.NET Core的 `[Authorize]` 特性
- 在请求进入Controller Action前由授权中间件检查
- 验证JWT中是否包含对应的权限Claim
- 这是**强制性**的安全边界，即使前端绕过，后端也会拒绝

### 8.2 Products.razor.cs - 客户端UI授权

在 [Products.razor.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Pages/Catalog/Products.razor.cs#L44-L59) 的 `OnInitializedAsync` 中，通过 `IAuthorizationService` 进行**客户端UI层授权**：

```csharp
protected override async Task OnInitializedAsync()
{
    _currentUser = await _authenticationManager.CurrentUser();
    _canCreateProducts = (await _authorizationService
        .AuthorizeAsync(_currentUser, Permissions.Products.Create)).Succeeded;
    _canEditProducts = (await _authorizationService
        .AuthorizeAsync(_currentUser, Permissions.Products.Edit)).Succeeded;
    _canDeleteProducts = (await _authorizationService
        .AuthorizeAsync(_currentUser, Permissions.Products.Delete)).Succeeded;
    _canExportProducts = (await _authorizationService
        .AuthorizeAsync(_currentUser, Permissions.Products.Export)).Succeeded;
    _canSearchProducts = (await _authorizationService
        .AuthorizeAsync(_currentUser, Permissions.Products.Search)).Succeeded;
    
    // 新增：检查归档权限
    _canArchiveProducts = (await _authorizationService
        .AuthorizeAsync(_currentUser, Permissions.Products.Archive)).Succeeded;
}
```

这些布尔值用于控制UI元素的显示，在 [Products.razor](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Pages/Catalog/Products.razor) 中：

```razor
@if (_canCreateProducts)
{
    <MudButton Color="Color.Primary" ...>@_localizer["Add Product"]</MudButton>
}

@if (_canExportProducts)
{
    <MudButton Color="Color.Success" ...>@_localizer["Export to Excel"]</MudButton>
}

@if (_canDeleteProducts)
{
    <MudIconButton ... OnClick="@(() => Delete(context.Id))" />
}

@if (_canArchiveProducts)
{
    <MudIconButton ... OnClick="@(() => Archive(context.Id))" />
}
```

**授权层说明**：
- **属于客户端UI层授权**
- 基于 `IAuthorizationService.AuthorizeAsync()` 方法
- 主要用于控制UI元素的显示/隐藏（按钮、菜单等）
- 提升用户体验，避免用户点击后才发现无权限
- **不是安全边界**，仅用于UI控制，真正的安全检查在服务端

### 8.3 两层授权对比

| 层面 | ProductsController | Products.razor.cs |
|------|-------------------|-------------------|
| **位置** | 服务端API层 | 客户端UI层 |
| **实现方式** | `[Authorize(Policy = "...")]` 特性 | `IAuthorizationService.AuthorizeAsync()` |
| **作用** | 强制安全检查，阻止未授权访问 | 控制UI元素可见性，提升用户体验 |
| **可绕过性** | 不可绕过（真正的安全边界） | 可通过技术手段绕过（非安全边界） |
| **检查时机** | 请求到达Action方法前 | 页面初始化/渲染时 |
| **失败处理** | 返回403 Forbidden | 隐藏按钮/禁用操作 |

---

## 九、新增 `Permissions.Products.Archive` 完整步骤总结

### 步骤1：添加权限常量
在 [Permissions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Shared/Constants/Permission/Permissions.cs#L12-L20) 的 `Products` 类中添加：
```csharp
public const string Archive = "Permissions.Products.Archive";
```

### 步骤2：服务端自动注册Policy
无需额外代码，[ServiceCollectionExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/ServiceCollectionExtensions.cs#L387-L398) 的反射机制会自动发现并注册。

### 步骤3：客户端自动注册Policy
无需额外代码，[WebAssemblyHostBuilderExtensions.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Client/Extensions/WebAssemblyHostBuilderExtensions.cs#L111-L121) 的反射机制会自动发现并注册。

### 步骤4：在ProductsController添加归档接口
```csharp
[Authorize(Policy = Permissions.Products.Archive)]
[HttpPost("{id}/archive")]
public async Task<IActionResult> Archive(int id)
{
    // 实现归档逻辑
    return Ok(await _mediator.Send(new ArchiveProductCommand { Id = id }));
}
```

### 步骤5：在Products.razor.cs添加权限检查
```csharp
private bool _canArchiveProducts;

// 在OnInitializedAsync中添加
_canArchiveProducts = (await _authorizationService
    .AuthorizeAsync(_currentUser, Permissions.Products.Archive)).Succeeded;
```

### 步骤6：在Products.razor添加归档按钮
```razor
@if (_canArchiveProducts)
{
    <MudIconButton ... OnClick="@(() => Archive(context.Id))" />
}
```

### 步骤7：在权限管理界面自动展示
无需额外代码，[ClaimsHelper.GetAllPermissions](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Helpers/ClaimExtensions.cs#L16-L44) 的反射机制会自动将 `Archive` 权限添加到待选列表中。

---

## 十、权限流转全景图

```
权限定义 → 服务端Policy注册 → 客户端Policy注册
    ↓
角色权限管理(UI勾选) → UpdatePermissionsAsync → 写入AspNetRoleClaims表
    ↓
用户登录 → GetClaimsAsync(收集角色权限) → 生成JWT(包含权限Claims)
    ↓
客户端存储Token → 解析Claims → UI授权检查(显示/隐藏按钮)
    ↓
API请求 → 服务端[Authorize]检查 → 执行/拒绝
    ↓
权限变更 → SignalR通知 → 刷新Token/强制登出
```
