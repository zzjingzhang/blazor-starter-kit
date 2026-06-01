# DocumentExtendedAttributes 动态泛型机制分析

## 1. 泛型继承链分析

### 1.1 控制器继承链

从 [DocumentExtendedAttributesController](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/ExtendedAttributes/Misc/DocumentExtendedAttributesController.cs#L12-L12) 开始的泛型继承链：

```
DocumentExtendedAttributesController
    ↓ 继承
ExtendedAttributesController<int, int, Document, DocumentExtendedAttribute>
    ↓ 继承
BaseApiController<ExtendedAttributesController<int, int, Document, DocumentExtendedAttribute>>
    ↓ 继承
ControllerBase (ASP.NET Core)
```

### 1.2 泛型参数含义

[ExtendedAttributesController](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/ExtendedAttributes/Base/ExtendedAttributesController.cs#L19-L24) 的四个泛型参数：

| 参数位置 | 泛型参数 | 实际类型 | 含义 |
|---------|---------|---------|------|
| TId | `TId` | `int` | 扩展属性主键类型 |
| TEntityId | `TEntityId` | `int` | 关联实体主键类型 |
| TEntity | `TEntity` | `Document` | 关联实体类型 |
| TExtendedAttribute | `TExtendedAttribute` | `DocumentExtendedAttribute` | 扩展属性实体类型 |

### 1.3 泛型约束

```csharp
where TEntity : AuditableEntity<TEntityId>, IEntityWithExtendedAttributes<TExtendedAttribute>, IEntity<TEntityId>
where TExtendedAttribute : AuditableEntityExtendedAttribute<TId, TEntityId, TEntity>, IEntity<TId>
where TId : IEquatable<TId>
```

### 1.4 领域实体基类

[AuditableEntityExtendedAttribute](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Contracts/AuditableEntityExtendedAttribute.cs#L7-L54) 是所有扩展属性的基类，包含：

- `EntityId` - 关联实体ID
- `Entity` - 关联实体导航属性
- `Type` - 扩展属性类型（Decimal/Text/DateTime/Json）
- `Key` - 属性键
- `Text`/`Decimal`/`DateTime`/`Json` - 不同类型的值存储
- `Group`/`Description`/`IsActive` 等元数据

---

## 2. BaseController 各 HTTP 方法构造的泛型 MediatR 请求

[ExtendedAttributesController](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/ExtendedAttributes/Base/ExtendedAttributesController.cs#L29-L95) 中每个 HTTP 方法都构造对应的泛型 MediatR 请求：

| HTTP 方法 | 路由 | MediatR 请求类型 |
|----------|------|-----------------|
| `GET` | `/` | `GetAllExtendedAttributesQuery<TId, TEntityId, TEntity, TExtendedAttribute>` |
| `GET` | `/by-entity/{entityId}` | `GetAllExtendedAttributesByEntityIdQuery<TId, TEntityId, TEntity, TExtendedAttribute>(entityId)` |
| `GET` | `/{id}` | `GetExtendedAttributeByIdQuery<TId, TEntityId, TEntity, TExtendedAttribute> { Id = id }` |
| `POST` | `/` | `AddEditExtendedAttributeCommand<TId, TEntityId, TEntity, TExtendedAttribute>` |
| `DELETE` | `/{id}` | `DeleteExtendedAttributeCommand<TId, TEntityId, TEntity, TExtendedAttribute> { Id = id }` |
| `GET` | `/export` | `ExportExtendedAttributesQuery<TId, TEntityId, TEntity, TExtendedAttribute>(...)` |

以 Document 为例，实际构造的请求类型为：
- `GetAllExtendedAttributesQuery<int, int, Document, DocumentExtendedAttribute>`
- `AddEditExtendedAttributeCommand<int, int, Document, DocumentExtendedAttribute>`

---

## 3. AddExtendedAttributesHandlers 反射注册处理器机制

[ServiceCollectionExtensions.AddExtendedAttributesHandlers](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Extensions/ServiceCollectionExtensions.cs#L27-L112) 采用反射扫描 + 泛型类型构造的方式自动注册所有处理器。

### 3.1 扫描逻辑

```csharp
var extendedAttributeTypes = typeof(IEntity)
    .Assembly
    .GetExportedTypes()
    .Where(t => t.IsClass && !t.IsAbstract && t.BaseType?.IsGenericType == true)
    .Select(t => new
    {
        BaseGenericType = t.BaseType,
        CurrentType = t
    })
    .Where(t => t.BaseGenericType?.GetGenericTypeDefinition() == typeof(AuditableEntityExtendedAttribute<,,>))
    .ToList();
```

**扫描条件**：
- 是具体类（非抽象、非接口）
- 基类是泛型类型
- 基类的泛型定义是 `AuditableEntityExtendedAttribute<,,>`

### 3.2 泛型参数构造

对于每个找到的扩展属性类型，提取其基类的3个泛型参数（TId, TEntityId, TEntity），再加上当前类型本身，组成4个泛型参数：

```csharp
var extendedAttributeTypeGenericArguments = extendedAttributeType.BaseGenericType.GetGenericArguments().ToList();
extendedAttributeTypeGenericArguments.Add(extendedAttributeType.CurrentType);
// 结果: [TId, TEntityId, TEntity, TExtendedAttribute]
```

### 3.3 注册的处理器类型

为每个扩展属性实体注册以下6种处理器：

| 处理器 | 请求类型 | 响应类型 |
|--------|---------|---------|
| `AddEditExtendedAttributeCommandHandler<,,,>` | `AddEditExtendedAttributeCommand<,,,>` | `Result<TId>` |
| `DeleteExtendedAttributeCommandHandler<,,,>` | `DeleteExtendedAttributeCommand<,,,>` | `Result<TId>` |
| `GetAllExtendedAttributesByEntityIdQueryHandler<,,,>` | `GetAllExtendedAttributesByEntityIdQuery<,,,>` | `Result<List<GetAllExtendedAttributesByEntityIdResponse<,>>>` |
| `GetExtendedAttributeByIdQueryHandler<,,,>` | `GetExtendedAttributeByIdQuery<,,,>` | `Result<GetExtendedAttributeByIdResponse<,>>` |
| `GetAllExtendedAttributesQueryHandler<,,,>` | `GetAllExtendedAttributesQuery<,,,>` | `Result<List<GetAllExtendedAttributesResponse<,>>>` |
| `ExportExtendedAttributesQueryHandler<,,,>` | `ExportExtendedAttributesQuery<,,,>` | `Result<string>` |

### 3.4 注册代码示例（以 AddEdit 为例）

```csharp
var tRequest = typeof(AddEditExtendedAttributeCommand<,,,>).MakeGenericType(extendedAttributeTypeGenericArguments.ToArray());
var tResponse = typeof(Result<>).MakeGenericType(extendedAttributeTypeGenericArguments.First());
var serviceType = typeof(IRequestHandler<,>).MakeGenericType(tRequest, tResponse);
var implementationType = typeof(AddEditExtendedAttributeCommandHandler<,,,>).MakeGenericType(extendedAttributeTypeGenericArguments.ToArray());
services.AddScoped(serviceType, implementationType);
```

**设计亮点**：通过 `MakeGenericType` 动态构造闭合泛型类型，实现"定义一次，自动适配所有扩展属性"。

---

## 4. AddExtendedAttributesValidators 注册验证器机制

[MvcBuilderExtensions.AddExtendedAttributesValidators](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Extensions/MvcBuilderExtensions.cs#L19-L44) 采用类似的反射机制注册验证器。

### 4.1 扫描逻辑

```csharp
var addEditExtendedAttributeCommandValidatorType = typeof(AddEditExtendedAttributeCommandValidator<,,,>);
var validatorTypes = addEditExtendedAttributeCommandValidatorType
    .Assembly
    .GetExportedTypes()
    .Where(t => t.IsClass && !t.IsAbstract && t.BaseType?.IsGenericType == true)
    .Select(t => new
    {
        BaseGenericType = t.BaseType,
        CurrentType = t
    })
    .Where(t => t.BaseGenericType?.GetGenericTypeDefinition() == typeof(AddEditExtendedAttributeCommandValidator<,,,>))
    .ToList();
```

**扫描条件**：基类的泛型定义是 `AddEditExtendedAttributeCommandValidator<,,,>`

### 4.2 Document 验证器示例

[AddEditDocumentExtendedAttributeCommandValidator](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Validators/Features/ExtendedAttributes/Commands/AddEdit/AddEditDocumentExtendedAttributeCommandValidator.cs#L7-L13)：

```csharp
public class AddEditDocumentExtendedAttributeCommandValidator 
    : AddEditExtendedAttributeCommandValidator<int, int, Document, DocumentExtendedAttribute>
{
    public AddEditDocumentExtendedAttributeCommandValidator(
        IStringLocalizer<AddEditExtendedAttributeCommandValidatorLocalization> localizer) 
        : base(localizer)
    {
        // you can override the validation rules here
    }
}
```

### 4.3 注册逻辑

```csharp
var addEditExtendedAttributeCommandType = typeof(AddEditExtendedAttributeCommand<,,,>).MakeGenericType(validatorType.BaseGenericType.GetGenericArguments());
var iValidator = typeof(IValidator<>).MakeGenericType(addEditExtendedAttributeCommandType);
services.AddScoped(iValidator, validatorType.CurrentType);
```

**关键点**：
- 基类 [AddEditExtendedAttributeCommandValidator](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Validators/Features/ExtendedAttributes/Commands/AddEdit/AddEditExtendedAttributeCommandValidator.cs#L15-L53) 定义通用验证规则
- 子类可以重写验证规则（目前 Document 的验证器未重写）
- 注册时将闭合泛型命令类型与具体验证器实现关联

---

## 5. AddEditExtendedAttributeCommandHandler 校验与缓存逻辑

[AddEditExtendedAttributeCommandHandler](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/ExtendedAttributes/Commands/AddEdit/AddEditExtendedAttributeCommand.cs#L62-L137) 是处理创建/更新的核心处理器。

### 5.1 同一 EntityId 下 Key 唯一性校验

```csharp
if (await _unitOfWork.Repository<TExtendedAttribute>().Entities
    .Where(x => !x.Id.Equals(command.Id) && x.EntityId!.Equals(command.EntityId))
    .AnyAsync(p => p.Key == command.Key, cancellationToken))
{
    return await Result<TId>.FailAsync(_localizer["Extended Attribute with this Key already exists."]);
}
```

**校验逻辑**：
- 排除自身（`!x.Id.Equals(command.Id)`）
- 限定同一实体（`x.EntityId.Equals(command.EntityId)`）
- 检查 Key 是否重复

### 5.2 创建逻辑（Id == default）

```csharp
var extendedAttribute = _mapper.Map<TExtendedAttribute>(command);
await _unitOfWork.Repository<TExtendedAttribute>().AddAsync(extendedAttribute);
await _unitOfWork.CommitAndRemoveCache(cancellationToken, 
    ApplicationConstants.Cache.GetAllEntityExtendedAttributesCacheKey(typeof(TEntity).Name));

// 删除所有关联实体的缓存
var cacheKeys = await _unitOfWork.Repository<TExtendedAttribute>().Entities.Select(x =>
    ApplicationConstants.Cache.GetAllEntityExtendedAttributesByEntityIdCacheKey(
        typeof(TEntity).Name, x.Entity.Id)).Distinct().ToArrayAsync(cancellationToken);
await _unitOfWork.CommitAndRemoveCache(cancellationToken, cacheKeys);
```

### 5.3 更新逻辑（Id != default）

```csharp
var extendedAttribute = await _unitOfWork.Repository<TExtendedAttribute>().GetByIdAsync(command.Id);
if (extendedAttribute != null)
{
    // 字段赋值：null 值保留原值
    extendedAttribute.Key = command.Key;
    extendedAttribute.EntityId = command.EntityId;
    extendedAttribute.Type = command.Type;
    extendedAttribute.Text = command.Text ?? extendedAttribute.Text;
    extendedAttribute.Decimal = command.Decimal ?? extendedAttribute.Decimal;
    extendedAttribute.DateTime = command.DateTime ?? extendedAttribute.DateTime;
    extendedAttribute.Json = command.Json ?? extendedAttribute.Json;
    extendedAttribute.ExternalId = command.ExternalId ?? extendedAttribute.ExternalId;
    extendedAttribute.Group = command.Group ?? extendedAttribute.Group;
    extendedAttribute.Description = command.Description ?? extendedAttribute.Description;
    extendedAttribute.IsActive = command.IsActive;
    
    await _unitOfWork.Repository<TExtendedAttribute>().UpdateAsync(extendedAttribute);
    // 同样的缓存清理逻辑...
}
```

**字段更新策略**：
- 必选字段直接覆盖（Key, EntityId, Type, IsActive）
- 可选字段使用空合并运算符，null 时保留原值（`command.Text ?? extendedAttribute.Text`）

### 5.4 缓存清理策略

每次变更后清理两类缓存：
1. **全局缓存**：`GetAllEntityExtendedAttributesCacheKey(typeof(TEntity).Name)` - 该类型所有扩展属性的缓存
2. **实体级缓存**：所有 `GetAllEntityExtendedAttributesByEntityIdCacheKey(typeof(TEntity).Name, entityId)` - 每个关联实体的缓存

---

## 6. GetAllExtendedAttributesByEntityIdQuery 缓存读取逻辑

[GetAllExtendedAttributesByEntityIdQueryHandler](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/ExtendedAttributes/Queries/GetAllByEntityId/GetAllExtendedAttributesByEntityIdQuery.cs#L31-L55) 采用 LazyCache 实现缓存。

### 6.1 缓存键构造

```csharp
ApplicationConstants.Cache.GetAllEntityExtendedAttributesByEntityIdCacheKey(
    typeof(TEntity).Name, request.EntityId)
```

### 6.2 缓存读写逻辑

```csharp
Func<Task<List<TExtendedAttribute>>> getAllExtendedAttributesByEntityId = () => 
    _unitOfWork.Repository<TExtendedAttribute>().Entities
        .Where(x => x.EntityId.Equals(request.EntityId))
        .ToListAsync(cancellationToken);

var extendedAttributeList = await _cache.GetOrAddAsync(
    cacheKey, 
    getAllExtendedAttributesByEntityId);
```

**GetOrAddAsync 模式**：
- 缓存存在：直接返回缓存值
- 缓存不存在：执行委托查询数据库，写入缓存后返回

### 6.3 过滤逻辑

```csharp
.Where(x => x.EntityId.Equals(request.EntityId))
```

仅通过 EntityId 过滤，不支持 Group 或其他条件过滤（这是 ByEntityId 查询的设计限制）。

---

## 7. ExportExtendedAttributesQuery 过滤与导出逻辑

[ExportExtendedAttributesQueryHandler](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/ExtendedAttributes/Queries/Export/ExportExtendedAttributesQuery.cs#L47-L126) 处理导出请求，支持复杂过滤和多类型值导出。

### 7.1 过滤条件（Specification 模式）

[ExtendedAttributeFilterSpecification](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Specifications/ExtendedAttribute/ExtendedAttributeFilterSpecification.cs#L8-L43) 定义过滤条件：

```csharp
Criteria = p =>
    (p.EntityId.Equals(request.EntityId) || request.EntityId.Equals(default))
    && (!request.OnlyCurrentGroup || request.CurrentGroup.Equals(p.Group));

if (request.IncludeEntity)
{
    Includes.Add(i => i.Entity);
}
```

**过滤参数**：
| 参数 | 作用 |
|------|------|
| `EntityId` | 按关联实体ID过滤，default(TEntityId) 表示不过滤 |
| `OnlyCurrentGroup` + `CurrentGroup` | 按 Group 字段过滤 |
| `IncludeEntity` | 是否 Include 关联实体（用于导出实体元数据） |

### 7.2 SearchString 内存过滤

由于 EF Core 表达式树不支持 null 传播运算符，SearchString 在内存中过滤：

```csharp
if (!string.IsNullOrWhiteSpace(request.SearchString))
{
    extendedAttributes = extendedAttributes.Where(p =>
            p.Key.Contains(request.SearchString, ...)
            || p.Decimal?.ToString().Contains(...) == true
            || p.Text?.Contains(...) == true
            || p.DateTime?.ToString(...).Contains(...) == true
            || p.Json?.Contains(...) == true
            || p.ExternalId?.Contains(...) == true
            || p.Description?.Contains(...) == true
            || p.Group?.Contains(...) == true)
        .ToList();
}
```

### 7.3 不同 Type 的 Value 导出

根据 `Type` 字段动态选择对应的值列导出：

```csharp
{_localizer["Value"], item => item.Type switch
{
    EntityExtendedAttributeType.Decimal => item.Decimal,
    EntityExtendedAttributeType.Text => item.Text,
    EntityExtendedAttributeType.DateTime => item.DateTime != null 
        ? DateTime.SpecifyKind((DateTime)item.DateTime, DateTimeKind.Utc).ToLocalTime().ToString("G", ...) 
        : string.Empty,
    EntityExtendedAttributeType.Json => item.Json,
    _ => throw new ArgumentOutOfRangeException(...)
}}
```

**导出列映射**：
- Id, EntityId, Type, Key, **Value** (动态), ExternalId, Group, Description, IsActive
- 可选 IncludeEntity 时增加：EntityCreatedBy, EntityCreatedOn, EntityLastModifiedBy 等

---

## 8. Json 类型校验中注释掉的 MustBeJson 对测试边界的影响

### 8.1 当前校验逻辑

在 [AddEditExtendedAttributeCommandValidator](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Validators/Features/ExtendedAttributes/Commands/AddEdit/AddEditExtendedAttributeCommandValidator.cs#L46-L51) 中：

```csharp
When(request => request.Type == EntityExtendedAttributeType.Json, () =>
{
    //RuleFor(request => request.Json).MustBeJson(new JsonValidator<...>(jsonSerializer))
    //    .WithMessage(x => string.Format(localizer["Json value must be a valid json string using {0} type!"], x.Type.ToString()));
    RuleFor(request => request.Json).NotNull().WithMessage(...);
});
```

**当前仅校验**：`Json` 字段不为 null

**被注释的校验**：`MustBeJson` - 验证字符串是有效的 JSON 格式

### 8.2 MustBeJson 实现

[JsonValidator](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Validators/JsonValidator.cs#L7-L38) 的验证逻辑：

```csharp
public override bool IsValid(ValidationContext<T> context, string value)
{
    var isJson = true;
    value = value.Trim();
    try
    {
        _jsonSerializer.Deserialize<object>(value);
    }
    catch
    {
        isJson = false;
    }
    isJson = isJson && value.StartsWith("{") && value.EndsWith("}")
             || value.StartsWith("[") && value.EndsWith("]");

    return isJson;
}
```

**验证条件**：
1. 能被 `IJsonSerializer.Deserialize<object>()` 成功解析
2. 以 `{}` 或 `[]` 包裹（JSON 对象或数组）

### 8.3 注释掉 MustBeJson 对测试边界的影响

| 测试场景 | 注释前（启用MustBeJson） | 注释后（仅NotNull） | 风险说明 |
|---------|------------------------|-------------------|---------|
| `null` | ❌ 失败 | ❌ 失败 | 行为一致 |
| `""` (空字符串) | ❌ 失败（Trim后无法解析） | ✅ 通过 | **允许空字符串，可能导致数据不一致** |
| `"   "` (空白) | ❌ 失败 | ✅ 通过 | **允许空白字符串** |
| `"invalid json"` | ❌ 失败 | ✅ 通过 | **允许非JSON格式文本** |
| `"123"` (数字) | ❌ 失败（非{}或[]包裹） | ✅ 通过 | **允许JSON原语而非结构** |
| `"\"string\""` (字符串) | ❌ 失败（非{}或[]包裹） | ✅ 通过 | **允许JSON字符串而非结构** |
| `"{}"` (空对象) | ✅ 通过 | ✅ 通过 | 行为一致 |
| `"[]"` (空数组) | ✅ 通过 | ✅ 通过 | 行为一致 |
| `"{\"key\": \"value\"}"` | ✅ 通过 | ✅ 通过 | 行为一致 |
| `"{invalid}"` (语法错误) | ❌ 失败 | ✅ 通过 | **允许语法错误的JSON** |

### 8.4 边界影响总结

1. **数据完整性风险**：无效 JSON 字符串可能存入数据库，后续消费代码（如反序列化）会抛出异常
2. **测试覆盖盲区**：单元测试如果只测试 `NotNull` 路径，无法发现 JSON 格式问题
3. **向后兼容性**：注释后允许了之前被拒绝的输入，需要确认是否有业务场景需要存储非标准 JSON
4. **可能原因**：构造函数中 `IJsonSerializer` 依赖也被注释，可能是序列化器未正确配置导致临时禁用

---

## 总结

DocumentExtendedAttributes 的动态泛型机制通过以下设计实现高复用性：

1. **四层泛型控制器链**：从具体控制器到通用基类，泛型参数贯穿始终
2. **反射驱动的依赖注册**：`AddExtendedAttributesHandlers` 和 `AddExtendedAttributesValidators` 自动扫描并注册所有扩展属性的处理器和验证器
3. **MediatR 请求泛型化**：每个 HTTP 操作对应一个泛型请求类型，处理器通过反射自动适配
4. **缓存与变更跟踪**：创建/更新后自动清理全局和实体级缓存
5. **灵活的导出机制**：根据 Type 动态映射 Value 列，支持多条件过滤

**潜在改进点**：
- 恢复 `MustBeJson` 校验（需要解决 `IJsonSerializer` 依赖注入问题）
- 考虑将 SearchString 过滤移至数据库层面（EF Core 支持可空表达式后）
