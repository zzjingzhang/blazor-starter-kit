# BlazorHero 审计Trail 技术分析文档

## 目录

1. [整体流程概述](#整体流程概述)
2. [入口：从Commit到SaveChangesAsync](#入口从commit到savechangesasync)
3. [BlazorHeroContext：设置IAuditableEntity审计字段](#blazorherocontext设置iauditableentity审计字段)
4. [AuditableContext.OnBeforeSaveChanges：核心审计逻辑](#auditablecontextonbeforesavechanges核心审计逻辑)
5. [AuditEntry.ToAudit：序列化审计数据](#auditentrytoaudit序列化审计数据)
6. [AuditableContext.OnAfterSaveChanges：处理临时主键](#auditablecontextonaftersavechanges处理临时主键)
7. [AuditsController与AuditService：查询与导出](#auditscontroller与auditservice查询与导出)
8. [UserId为空时的边界情况分析](#userid为空时的边界情况分析)
9. [新增实体临时主键的测试设计](#新增实体临时主键的测试设计)

---

## 整体流程概述

当已登录用户修改 Product 或 Document 实体时，审计Trail的生成遵循以下完整流程：

```
用户操作 → UnitOfWork.Commit() / RoleClaimService.SaveAsync()
         ↓
BlazorHeroContext.SaveChangesAsync()
         ↓
    1. 设置 IAuditableEntity 的 CreatedBy/CreatedOn / LastModifiedBy/LastModifiedOn
         ↓
AuditableContext.SaveChangesAsync(userId, cancellationToken)
         ↓
    2. OnBeforeSaveChanges(userId)
       - 过滤需要审计的实体
       - 识别主键、临时属性、字段变更
       - 生成 AuditEntry 列表
       - 保存无临时属性的审计记录
         ↓
    3. base.SaveChangesAsync() - 实体数据真正持久化到数据库
         ↓
    4. OnAfterSaveChanges(auditEntries)
       - 填充新增实体的临时主键（数据库生成）
       - 保存剩余审计记录
         ↓
查询/导出 → AuditsController → AuditService
         ↓
    仅查询当前UserId的最近250条记录，支持按条件导出Excel
```

---

## 入口：从Commit到SaveChangesAsync

审计流程有两个主要入口点：

### 1. UnitOfWork.Commit 入口

[UnitOfWork.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Repositories/UnitOfWork.cs#L48-L51)

```csharp
public async Task<int> Commit(CancellationToken cancellationToken)
{
    return await _dbContext.SaveChangesAsync(cancellationToken);
}
```

### 2. RoleClaimService.SaveAsync 入口

[RoleClaimService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/Identity/RoleClaimService.cs#L67-L111)

```csharp
public async Task<Result<string>> SaveAsync(RoleClaimRequest request)
{
    if (request.Id == 0)
    {
        // 新增逻辑
        var roleClaim = _mapper.Map<BlazorHeroRoleClaim>(request);
        await _db.RoleClaims.AddAsync(roleClaim);
        await _db.SaveChangesAsync(_currentUserService.UserId);  // 直接传递userId
    }
    else
    {
        // 更新逻辑
        existingRoleClaim.ClaimType = request.Type;
        // ... 其他属性更新
        _db.RoleClaims.Update(existingRoleClaim);
        await _db.SaveChangesAsync(_currentUserService.UserId);  // 直接传递userId
    }
}
```

**注意**：`RoleClaimService` 直接调用带 `userId` 参数的 `SaveChangesAsync` 重载，而 `UnitOfWork` 调用的是无 `userId` 参数的版本，但最终都会进入 `BlazorHeroContext.SaveChangesAsync`。

---

## BlazorHeroContext：设置IAuditableEntity审计字段

[BlazorHeroContext.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/BlazorHeroContext.cs#L35-L60)

### 核心逻辑

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
{
    // 遍历所有实现IAuditableEntity的实体条目
    foreach (var entry in ChangeTracker.Entries<IAuditableEntity>().ToList())
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Entity.CreatedOn = _dateTimeService.NowUtc;
                entry.Entity.CreatedBy = _currentUserService.UserId;
                break;

            case EntityState.Modified:
                entry.Entity.LastModifiedOn = _dateTimeService.NowUtc;
                entry.Entity.LastModifiedBy = _currentUserService.UserId;
                break;
        }
    }

    // 根据UserId是否为空决定调用哪个base重载
    if (_currentUserService.UserId == null)
    {
        return await base.SaveChangesAsync(cancellationToken);
    }
    else
    {
        return await base.SaveChangesAsync(_currentUserService.UserId, cancellationToken);
    }
}
```

### IAuditableEntity 接口定义

[IAuditableEntity.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Contracts/IAuditableEntity.cs#L9-L18)

```csharp
public interface IAuditableEntity : IEntity
{
    string CreatedBy { get; set; }
    DateTime CreatedOn { get; set; }
    string LastModifiedBy { get; set; }
    DateTime? LastModifiedOn { get; set; }
}
```

### Product 和 Document 实体示例

[Product.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Entities/Catalog/Product.cs#L6-L18)

```csharp
public class Product : AuditableEntity<int>
{
    public string Name { get; set; }
    public string Barcode { get; set; }
    // ... 其他属性
}
```

[Document.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Entities/Misc/Document.cs#L6-L14)

```csharp
public class Document : AuditableEntityWithExtendedAttributes<int, int, Document, DocumentExtendedAttribute>
{
    public string Title { get; set; }
    public string Description { get; set; }
    // ... 其他属性
}
```

### 关键点分析

1. **时间来源**：使用 `_dateTimeService.NowUtc` 确保时间一致性，便于单元测试
2. **用户来源**：使用 `_currentUserService.UserId` 获取当前登录用户ID
3. **分支逻辑**：根据 `UserId` 是否为空决定审计流程的走向
4. **覆盖范围**：仅处理 `Added` 和 `Modified` 状态，`Deleted` 状态不在此处理

---

## AuditableContext.OnBeforeSaveChanges：核心审计逻辑

[AuditableContext.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/AuditableContext.cs#L30-L89)

### 方法签名

```csharp
private List<AuditEntry> OnBeforeSaveChanges(string userId)
```

### 完整逻辑分解

#### 1. 变更检测

```csharp
ChangeTracker.DetectChanges();
var auditEntries = new List<AuditEntry>();
```

确保所有变更都被EF Core检测到。

#### 2. 跳过不需要审计的条目

```csharp
foreach (var entry in ChangeTracker.Entries())
{
    if (entry.Entity is Audit || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
        continue;
    // ... 后续处理
}
```

**跳过条件**：

- `entry.Entity is Audit`：跳过审计实体自身，避免递归审计
- `entry.State == EntityState.Detached`：跳过未被上下文追踪的实体
- `entry.State == EntityState.Unchanged`：跳过未发生变化的实体

#### 3. 创建AuditEntry

```csharp
var auditEntry = new AuditEntry(entry)
{
    TableName = entry.Entity.GetType().Name,  // 表名 = 类名
    UserId = userId                           // 操作人ID
};
auditEntries.Add(auditEntry);
```

#### 4. 遍历实体属性进行分类处理

```csharp
foreach (var property in entry.Properties)
{
    // 处理临时属性（如数据库生成的主键）
    if (property.IsTemporary)
    {
        auditEntry.TemporaryProperties.Add(property);
        continue;
    }

    string propertyName = property.Metadata.Name;

    // 处理主键
    if (property.Metadata.IsPrimaryKey())
    {
        auditEntry.KeyValues[propertyName] = property.CurrentValue;
        continue;
    }

    // 根据实体状态处理字段差异
    switch (entry.State)
    {
        case EntityState.Added:
            auditEntry.AuditType = AuditType.Create;
            auditEntry.NewValues[propertyName] = property.CurrentValue;
            break;

        case EntityState.Deleted:
            auditEntry.AuditType = AuditType.Delete;
            auditEntry.OldValues[propertyName] = property.OriginalValue;
            break;

        case EntityState.Modified:
            // 仅记录实际发生变化的字段
            if (property.IsModified && property.OriginalValue?.Equals(property.CurrentValue) == false)
            {
                auditEntry.ChangedColumns.Add(propertyName);
                auditEntry.AuditType = AuditType.Update;
                auditEntry.OldValues[propertyName] = property.OriginalValue;
                auditEntry.NewValues[propertyName] = property.CurrentValue;
            }
            break;
    }
}
```

#### 5. 保存无临时属性的审计记录

```csharp
foreach (var auditEntry in auditEntries.Where(_ => !_.HasTemporaryProperties))
{
    AuditTrails.Add(auditEntry.ToAudit());
}
return auditEntries.Where(_ => _.HasTemporaryProperties).ToList();
```

### 关键点分析

1. **TemporaryProperties 机制**：
   - 新增实体（如Product）的主键通常由数据库生成（Identity列）
   - 在 `SaveChanges` 前，EF Core会为这些主键分配临时值
   - `property.IsTemporary` 标识这些需要在数据库写入后才能确定最终值的属性
   - 这些审计条目会被暂存，在 `OnAfterSaveChanges` 中处理

2. **Modified状态的精确比较**：
   - 使用 `property.IsModified` 检查EF Core标记为修改的属性
   - 使用 `property.OriginalValue?.Equals(property.CurrentValue) == false` 进行实际值比较
   - 双重检查确保只记录真正发生变化的字段

3. **主键识别**：
   - 使用 `property.Metadata.IsPrimaryKey()` 识别主键
   - 主键值单独存储在 `KeyValues` 字典中，便于后续查询和关联

---

## AuditEntry.ToAudit：序列化审计数据

[AuditEntry.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Models/Audit/AuditEntry.cs#L28-L42)

### AuditEntry 类结构

```csharp
public class AuditEntry
{
    public EntityEntry Entry { get; }
    public string UserId { get; set; }
    public string TableName { get; set; }
    public Dictionary<string, object> KeyValues { get; } = new();
    public Dictionary<string, object> OldValues { get; } = new();
    public Dictionary<string, object> NewValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();
    public AuditType AuditType { get; set; }
    public List<string> ChangedColumns { get; } = new();
    public bool HasTemporaryProperties => TemporaryProperties.Any();
    // ...
}
```

### ToAudit 序列化方法

```csharp
public Audit ToAudit()
{
    var audit = new Audit
    {
        UserId = UserId,
        Type = AuditType.ToString(),
        TableName = TableName,
        DateTime = DateTime.UtcNow,
        PrimaryKey = JsonConvert.SerializeObject(KeyValues),
        OldValues = OldValues.Count == 0 ? null : JsonConvert.SerializeObject(OldValues),
        NewValues = NewValues.Count == 0 ? null : JsonConvert.SerializeObject(NewValues),
        AffectedColumns = ChangedColumns.Count == 0 ? null : JsonConvert.SerializeObject(ChangedColumns)
    };
    return audit;
}
```

### Audit 实体结构

[Audit.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Models/Audit/Audit.cs#L6-L17)

```csharp
public class Audit : IEntity<int>
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public string Type { get; set; }
    public string TableName { get; set; }
    public DateTime DateTime { get; set; }
    public string OldValues { get; set; }
    public string NewValues { get; set; }
    public string AffectedColumns { get; set; }
    public string PrimaryKey { get; set; }
}
```

### 序列化策略分析

| 字段              | 序列化方式                                    | 空值处理          | 示例                                |
| ----------------- | --------------------------------------------- | ----------------- | ----------------------------------- |
| `PrimaryKey`      | `JsonConvert.SerializeObject(KeyValues)`      | 不可空            | `{"Id": 123}`                       |
| `OldValues`       | `JsonConvert.SerializeObject(OldValues)`      | Count=0时设为null | `{"Name": "OldName", "Rate": 10.5}` |
| `NewValues`       | `JsonConvert.SerializeObject(NewValues)`      | Count=0时设为null | `{"Name": "NewName", "Rate": 15.5}` |
| `AffectedColumns` | `JsonConvert.SerializeObject(ChangedColumns)` | Count=0时设为null | `["Name", "Rate"]`                  |

**注意**：

- `OldValues` 在Create操作中为null（新增无旧值）
- `NewValues` 在Delete操作中为null（删除无新值）
- `AffectedColumns` 仅在Update操作中有值

---

## AuditableContext.OnAfterSaveChanges：处理临时主键

[AuditableContext.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/AuditableContext.cs#L91-L112)

### 核心逻辑

```csharp
private Task OnAfterSaveChanges(List<AuditEntry> auditEntries, CancellationToken cancellationToken = new())
{
    if (auditEntries == null || auditEntries.Count == 0)
        return Task.CompletedTask;

    foreach (var auditEntry in auditEntries)
    {
        foreach (var prop in auditEntry.TemporaryProperties)
        {
            if (prop.Metadata.IsPrimaryKey())
            {
                // 数据库已生成真正的主键值
                auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
            }
            else
            {
                // 其他临时属性也需要填充
                auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
            }
        }
        // 现在可以序列化并保存审计记录
        AuditTrails.Add(auditEntry.ToAudit());
    }
    // 递归调用，但此时auditEntries为空，不会无限循环
    return SaveChangesAsync(cancellationToken);
}
```

### 新增实体（Product）的完整审计时序

```
1. 用户添加Product → context.Products.Add(newProduct)
   ↓
   newProduct.Id = 0 (CLR默认值)
   ↓
2. context.SaveChangesAsync() 被调用
   ↓
3. BlazorHeroContext.SaveChangesAsync
   - 设置 newProduct.CreatedOn, CreatedBy
   - 调用 base.SaveChangesAsync(userId)
   ↓
4. AuditableContext.SaveChangesAsync
   ↓
5. OnBeforeSaveChanges(userId)
   - ChangeTracker.DetectChanges()
   - 识别 newProduct 为 Added 状态
   - 识别 Id 为 IsTemporary = true
   - 将 Id 添加到 TemporaryProperties
   - 记录其他字段的 NewValues
   - 因为 HasTemporaryProperties = true，暂不保存
   - 返回 auditEntries 列表（包含这个新增条目）
   ↓
6. base.SaveChangesAsync(cancellationToken)
   - EF Core向数据库发送INSERT语句
   - 数据库生成真正的Id（如123）
   - EF Core更新实体的Id属性为123
   - 此时 prop.CurrentValue = 123，IsTemporary = false
   ↓
7. OnAfterSaveChanges(auditEntries)
   - 遍历TemporaryProperties
   - Id现在有了真正的值123
   - 更新 KeyValues["Id"] = 123
   - 调用 ToAudit() 序列化
   - 添加到 AuditTrails
   - 再次调用 SaveChangesAsync() 保存审计记录
```

---

## AuditsController与AuditService：查询与导出

### AuditsController API端点

[AuditsController.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/AuditsController.cs#L23-L47)

```csharp
[Authorize(Policy = Permissions.AuditTrails.View)]
[HttpGet]
public async Task<IActionResult> GetUserTrailsAsync()
{
    return Ok(await _auditService.GetCurrentUserTrailsAsync(_currentUserService.UserId));
}

[Authorize(Policy = Permissions.AuditTrails.Export)]
[HttpGet("export")]
public async Task<IActionResult> ExportExcel(string searchString = "",
    bool searchInOldValues = false, bool searchInNewValues = false)
{
    var data = await _auditService.ExportToExcelAsync(
        _currentUserService.UserId, searchString, searchInOldValues, searchInNewValues);
    return Ok(data);
}
```

### AuditService 查询逻辑

[AuditService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/AuditService.cs#L38-L43)

```csharp
public async Task<IResult<IEnumerable<AuditResponse>>> GetCurrentUserTrailsAsync(string userId)
{
    var trails = await _context.AuditTrails
        .Where(a => a.UserId == userId)           // 只查询当前用户
        .OrderByDescending(a => a.Id)             // 按ID倒序（最新在前）
        .Take(250)                                // 只取最近250条
        .ToListAsync();
    var mappedLogs = _mapper.Map<List<AuditResponse>>(trails);
    return await Result<IEnumerable<AuditResponse>>.SuccessAsync(mappedLogs);
}
```

**关键点**：

- `Where(a => a.UserId == userId)`：严格的数据权限隔离，用户只能看到自己的审计记录
- `OrderByDescending(a => a.Id)`：假设Id是自增主键，按时间倒序
- `Take(250)`：限制返回数量，防止查询过多数据

### AuditFilterSpecification 导出过滤逻辑

[AuditFilterSpecification.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Specifications/AuditFilterSpecification.cs#L6-L19)

```csharp
public class AuditFilterSpecification : HeroSpecification<Audit>
{
    public AuditFilterSpecification(string userId, string searchString,
        bool searchInOldValues, bool searchInNewValues)
    {
        if (!string.IsNullOrEmpty(searchString))
        {
            Criteria = p =>
                (p.TableName.Contains(searchString)                          // 搜索表名
                || searchInOldValues && p.OldValues.Contains(searchString)   // 可选：搜索旧值
                || searchInNewValues && p.NewValues.Contains(searchString))  // 可选：搜索新值
                && p.UserId == userId;                                        // 用户隔离
        }
        else
        {
            Criteria = p => p.UserId == userId;
        }
    }
}
```

### AuditService 导出逻辑

[AuditService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/AuditService.cs#L45-L65)

```csharp
public async Task<IResult<string>> ExportToExcelAsync(string userId,
    string searchString = "", bool searchInOldValues = false, bool searchInNewValues = false)
{
    var auditSpec = new AuditFilterSpecification(userId, searchString, searchInOldValues, searchInNewValues);
    var trails = await _context.AuditTrails
        .Specify(auditSpec)                          // 应用过滤规范
        .OrderByDescending(a => a.DateTime)          // 按时间倒序
        .ToListAsync();

    var data = await _excelService.ExportAsync(trails, sheetName: _localizer["Audit trails"],
        mappers: new Dictionary<string, Func<Audit, object>>
        {
            { _localizer["Table Name"], item => item.TableName },
            { _localizer["Type"], item => item.Type },
            { _localizer["Date Time (Local)"], item =>
                DateTime.SpecifyKind(item.DateTime, DateTimeKind.Utc)
                    .ToLocalTime().ToString("G", CultureInfo.CurrentCulture) },
            { _localizer["Date Time (UTC)"], item =>
                item.DateTime.ToString("G", CultureInfo.CurrentCulture) },
            { _localizer["Primary Key"], item => item.PrimaryKey },
            { _localizer["Old Values"], item => item.OldValues },
            { _localizer["New Values"], item => item.NewValues },
        });

    return await Result<string>.SuccessAsync(data: data);
}
```

**导出字段说明**：

- **Table Name**：实体类名（如Product、Document）
- **Type**：操作类型（Create、Update、Delete）
- **Date Time (Local)**：转换为用户本地时间
- **Date Time (UTC)**：UTC原始时间
- **Primary Key**：JSON格式的主键
- **Old Values**：JSON格式的旧值
- **New Values**：JSON格式的新值

---

## UserId为空时的边界情况分析

### 场景分析

当 `_currentUserService.UserId` 为空时（如未登录、后台任务、测试环境等），审计流程会发生变化。

### BlazorHeroContext 分支判断

[BlazorHeroContext.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/BlazorHeroContext.cs#L52-L59)

```csharp
if (_currentUserService.UserId == null)
{
    // UserId为空时，调用无userId的重载
    return await base.SaveChangesAsync(cancellationToken);
}
else
{
    // UserId非空时，传递userId进行完整审计
    return await base.SaveChangesAsync(_currentUserService.UserId, cancellationToken);
}
```

### AuditableContext 默认参数

[AuditableContext.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/AuditableContext.cs#L22-L28)

```csharp
public virtual async Task<int> SaveChangesAsync(string userId = null,
    CancellationToken cancellationToken = new())
{
    var auditEntries = OnBeforeSaveChanges(userId);  // userId = null
    var result = await base.SaveChangesAsync(cancellationToken);
    await OnAfterSaveChanges(auditEntries, cancellationToken);
    return result;
}
```

### 结果分析

| 字段                  | UserId非空时 | UserId为空时                                          |
| --------------------- | ------------ | ----------------------------------------------------- |
| 实体 `CreatedBy`      | 实际用户ID   | `null`                                                |
| 实体 `LastModifiedBy` | 实际用户ID   | `null`                                                |
| 审计记录 `UserId`     | 实际用户ID   | `null`                                                |
| 审计记录是否生成      | 是           | 是（但UserId为null）                                  |
| 查询时是否可见        | 对应用户可见 | 所有用户都不可见（因为查询条件 `a.UserId == userId`） |

### 潜在问题

1. **数据丢失**：UserId为空的审计记录无法被任何用户通过API查询到
2. **溯源困难**：无法确定是谁执行了操作
3. **后台任务**：如果是后台任务执行的修改，可能需要特殊处理（如设置系统用户ID）

### 改进建议

```csharp
// 在BlazorHeroContext中，可以考虑添加系统用户默认值
private string GetCurrentUserId()
{
    return _currentUserService.UserId ?? "System";  // 或其他系统标识符
}
```

---

## 新增实体临时主键的测试设计

### 测试目标

验证新增实体时，数据库生成的主键（临时属性）能被正确捕获并记录到审计Trail中。

### 测试场景设计

#### 场景1：新增Product实体（单主键）

**测试要点**：

- 新增Product时Id为0（默认值）
- SaveChanges后Id被数据库更新
- 审计记录中PrimaryKey应包含正确的Id值
- NewValues中不应缺失任何字段

**测试代码结构**：

```csharp
[Test]
public async Task AddProduct_Should_AuditTemporaryPrimaryKey()
{
    // Arrange
    var dbContext = CreateDbContext();
    var product = new Product
    {
        Name = "Test Product",
        Barcode = "TEST001",
        Rate = 99.99m
    };

    // Act - 添加实体
    dbContext.Products.Add(product);

    // 断言1：添加后Id仍为0（临时值）
    Assert.That(product.Id, Is.EqualTo(0));

    // Act - 保存更改
    await dbContext.SaveChangesAsync();

    // 断言2：保存后Id已被数据库更新
    Assert.That(product.Id, Is.GreaterThan(0));

    // 断言3：审计记录已生成且包含正确主键
    var audit = await dbContext.AuditTrails
        .OrderByDescending(a => a.Id)
        .FirstOrDefaultAsync(a => a.TableName == nameof(Product));

    Assert.That(audit, Is.Not.Null);
    Assert.That(audit.Type, Is.EqualTo("Create"));

    // 断言4：主键正确序列化
    var primaryKey = JsonConvert.DeserializeObject<Dictionary<string, object>>(audit.PrimaryKey);
    Assert.That(primaryKey["Id"], Is.EqualTo(product.Id));

    // 断言5：NewValues包含所有新增字段
    var newValues = JsonConvert.DeserializeObject<Dictionary<string, object>>(audit.NewValues);
    Assert.That(newValues["Name"], Is.EqualTo("Test Product"));
    Assert.That(newValues["Barcode"], Is.EqualTo("TEST001"));
    Assert.That(newValues["Rate"], Is.EqualTo(99.99m));
}
```

#### 场景2：新增带复合主键的实体

**测试要点**：

- 验证复合主键的所有字段都被正确捕获
- TemporaryProperties应包含所有临时主键字段

```csharp
[Test]
public async Task AddEntityWithCompositeKey_Should_AuditAllTemporaryKeys()
{
    // Arrange
    var entity = new EntityWithCompositeKey
    {
        // 数据库生成两个主键
        Name = "Test"
    };
    dbContext.Add(entity);

    // Act
    await dbContext.SaveChangesAsync();

    // Assert
    var audit = await dbContext.AuditTrails
        .FirstOrDefaultAsync(a => a.TableName == nameof(EntityWithCompositeKey));

    var primaryKey = JsonConvert.DeserializeObject<Dictionary<string, object>>(audit.PrimaryKey);
    Assert.That(primaryKey.ContainsKey("Key1"), Is.True);
    Assert.That(primaryKey.ContainsKey("Key2"), Is.True);
    Assert.That(primaryKey["Key1"], Is.EqualTo(entity.Key1));
    Assert.That(primaryKey["Key2"], Is.EqualTo(entity.Key2));
}
```

#### 场景3：批量新增多个实体

**测试要点**：

- 批量新增时每个实体的临时主键都能被正确处理
- OnAfterSaveChanges能正确处理多个AuditEntry

```csharp
[Test]
public async Task AddMultipleProducts_Should_AuditEachWithCorrectPrimaryKey()
{
    // Arrange
    var products = new[]
    {
        new Product { Name = "Product 1", Barcode = "BATCH001", Rate = 10m },
        new Product { Name = "Product 2", Barcode = "BATCH002", Rate = 20m },
        new Product { Name = "Product 3", Barcode = "BATCH003", Rate = 30m }
    };

    // Act
    await dbContext.Products.AddRangeAsync(products);
    await dbContext.SaveChangesAsync();

    // Assert
    var audits = await dbContext.AuditTrails
        .Where(a => a.TableName == nameof(Product))
        .OrderByDescending(a => a.Id)
        .Take(3)
        .ToListAsync();

    Assert.That(audits.Count, Is.EqualTo(3));

    for (int i = 0; i < audits.Count; i++)
    {
        var primaryKey = JsonConvert.DeserializeObject<Dictionary<string, object>>(audits[i].PrimaryKey);
        var expectedProduct = products.First(p => p.Name == $"Product {3 - i}");
        Assert.That(primaryKey["Id"], Is.EqualTo(expectedProduct.Id));
    }
}
```

#### 场景4：验证TemporaryProperties的正确识别

**测试要点**：

- 在OnBeforeSaveChanges中验证IsTemporary属性的正确设置
- 使用EF Core的InMemory数据库可能需要特殊处理（因为InMemory不生成Identity值）

```csharp
[Test]
public void OnBeforeSaveChanges_Should_IdentifyTemporaryProperties()
{
    // Arrange
    var dbContext = CreateDbContext();  // 使用真实数据库或配置ValueGeneratedOnAdd
    var product = new Product { Name = "Test", Rate = 10m };
    dbContext.Products.Add(product);

    // Act - 手动调用DetectChanges
    dbContext.ChangeTracker.DetectChanges();
    var entry = dbContext.Entry(product);

    // Assert
    var idProperty = entry.Property(p => p.Id);
    Assert.That(idProperty.IsTemporary, Is.True, "Id should be temporary before SaveChanges");
    Assert.That(idProperty.Metadata.IsPrimaryKey(), Is.True);
}
```

### 测试基础设施注意事项

1. **数据库提供程序**：
   - InMemory数据库不会生成Identity值，`IsTemporary`始终为false
   - 建议使用SQL Server LocalDB或SQLite进行集成测试
   - 确保主键配置为 `ValueGeneratedOnAdd()`

2. **CurrentUserService Mock**：

   ```csharp
   var mockCurrentUserService = new Mock<ICurrentUserService>();
   mockCurrentUserService.Setup(s => s.UserId).Returns("test-user-id");
   ```

3. **DateTimeService Mock**：
   ```csharp
   var mockDateTimeService = new Mock<IDateTimeService>();
   var testTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
   mockDateTimeService.Setup(s => s.NowUtc).Returns(testTime);
   ```

### 测试断言矩阵

| 测试场景   | 断言点               | 预期结果                 |
| ---------- | -------------------- | ------------------------ |
| 新增单实体 | PrimaryKey           | 包含数据库生成的Id       |
| 新增单实体 | NewValues            | 包含所有字段，无缺失     |
| 新增单实体 | AuditType            | "Create"                 |
| 新增单实体 | UserId               | 当前登录用户ID           |
| 新增单实体 | OldValues            | null                     |
| 新增单实体 | AffectedColumns      | null                     |
| 批量新增   | 审计记录数量         | 等于新增实体数量         |
| 批量新增   | 每条记录的PrimaryKey | 互不相同，对应各自实体Id |
| 复合主键   | PrimaryKey           | 包含所有主键字段         |
| UserId为空 | UserId字段           | null                     |

---

## 总结

### 审计系统的设计亮点

1. **分层架构**：BlazorHeroContext处理实体审计字段，AuditableContext处理审计Trail生成
2. **精确变更追踪**：Modified状态下双重检查确保只记录真正变更的字段
3. **临时属性处理**：OnBefore/OnAfterSaveChanges两阶段提交完美处理数据库生成主键
4. **数据权限隔离**：查询和导出严格按UserId过滤
5. **完整序列化**：使用JSON存储结构化数据，便于后续分析

### 潜在改进点

1. **UserId为空处理**：考虑为后台任务设置系统用户ID
2. **OldValues/NewValues查询**：当前使用字符串Contains搜索JSON，可能不够精确
3. **批量操作优化**：大批量操作时审计记录的生成可能影响性能
4. **审计记录完整性**：Deleted状态的实体在BlazorHeroContext中不会设置LastModifiedBy
