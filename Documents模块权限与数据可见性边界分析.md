# Documents模块权限与数据可见性边界分析

## 一、模块架构总览

Documents模块采用分层架构设计，主要由以下核心组件构成：

| 组件 | 职责 | 文件位置 |
|------|------|----------|
| DocumentsController | API入口，权限控制 | [DocumentsController.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/Misc/DocumentsController.cs) |
| GetAllDocumentsQueryHandler | 列表查询业务逻辑 | [GetAllDocumentsQuery.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Queries/GetAll/GetAllDocumentsQuery.cs) |
| DocumentFilterSpecification | 查询过滤规则 | [DocumentFilterSpecification.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Specifications/Misc/DocumentFilterSpecification.cs) |
| CurrentUserService | 当前用户信息提供 | [CurrentUserService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Services/CurrentUserService.cs) |
| AddEditDocumentCommandHandler | 新增/编辑业务逻辑 | [AddEditDocumentCommand.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Commands/AddEdit/AddEditDocumentCommand.cs) |
| UploadService | 文件上传服务 | [UploadService.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/UploadService.cs) |
| DeleteDocumentCommandHandler | 删除业务逻辑 | [DeleteDocumentCommand.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Commands/Delete/DeleteDocumentCommand.cs) |
| Document实体 | 数据模型 | [Document.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Entities/Misc/Document.cs) |

---

## 二、Document实体结构

[Document.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Entities/Misc/Document.cs#L6-L14) 定义如下：

```csharp
public class Document : AuditableEntityWithExtendedAttributes<int, int, Document, DocumentExtendedAttribute>
{
    public string Title { get; set; }
    public string Description { get; set; }
    public bool IsPublic { get; set; } = false;  // 公开标识，默认为私有
    public string URL { get; set; }
    public int DocumentTypeId { get; set; }
    public virtual DocumentType DocumentType { get; set; }
}
```

**继承关系**：
- `Document` → `AuditableEntityWithExtendedAttributes` → `AuditableEntity` → `IAuditableEntity`
- 通过 [IAuditableEntity.cs](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Domain/Contracts/IAuditableEntity.cs#L11-L17) 获得审计属性：
  - `string CreatedBy { get; set; }`
  - `DateTime CreatedOn { get; set; }`
  - `string LastModifiedBy { get; set; }`
  - `DateTime? LastModifiedOn { get; set; }`

---

## 三、列表查询过滤规则（公开 vs 私有文档）

### 3.1 控制器入口 - GetAll

[DocumentsController.GetAll](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Controllers/Utilities/Misc/DocumentsController.cs#L23-L29)：

```csharp
[Authorize(Policy = Permissions.Documents.View)]
[HttpGet]
public async Task<IActionResult> GetAll(int pageNumber, int pageSize, string searchString)
{
    var docs = await _mediator.Send(new GetAllDocumentsQuery(pageNumber, pageSize, searchString));
    return Ok(docs);
}
```

**权限控制**：使用 `[Authorize(Policy = Permissions.Documents.View)]` 确保只有具备文档查看权限的用户才能访问。

### 3.2 查询处理器 - GetAllDocumentsQueryHandler

[GetAllDocumentsQueryHandler.Handle](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Queries/GetAll/GetAllDocumentsQuery.cs#L42-L62)：

```csharp
public async Task<PaginatedResult<GetAllDocumentsResponse>> Handle(GetAllDocumentsQuery request, CancellationToken cancellationToken)
{
    Expression<Func<Document, GetAllDocumentsResponse>> expression = e => new GetAllDocumentsResponse
    {
        Id = e.Id,
        Title = e.Title,
        CreatedBy = e.CreatedBy,
        IsPublic = e.IsPublic,
        CreatedOn = e.CreatedOn,
        Description = e.Description,
        URL = e.URL,
        DocumentType = e.DocumentType.Name,
        DocumentTypeId = e.DocumentTypeId
    };
    var docSpec = new DocumentFilterSpecification(request.SearchString, _currentUserService.UserId);
    var data = await _unitOfWork.Repository<Document>().Entities
       .Specify(docSpec)
       .Select(expression)
       .ToPaginatedListAsync(request.PageNumber, request.PageSize);
    return data;
}
```

**关键点**：
- 注入 `ICurrentUserService` 获取当前用户ID
- 创建 `DocumentFilterSpecification` 时传入 `searchString` 和 当前用户ID `userId`
- 使用 `.Specify(docSpec)` 应用过滤规范

### 3.3 过滤规范 - DocumentFilterSpecification

[DocumentFilterSpecification](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Specifications/Misc/DocumentFilterSpecification.cs#L6-L19) 是数据可见性的核心：

```csharp
public class DocumentFilterSpecification : HeroSpecification<Document>
{
    public DocumentFilterSpecification(string searchString, string userId)
    {
        if (!string.IsNullOrEmpty(searchString))
        {
            Criteria = p => (p.Title.Contains(searchString) || p.Description.Contains(searchString)) 
                         && (p.IsPublic == true || (p.IsPublic == false && p.CreatedBy == userId));
        }
        else
        {
            Criteria = p => (p.IsPublic == true || (p.IsPublic == false && p.CreatedBy == userId));
        }
    }
}
```

### 3.4 过滤规则详解

**逻辑表达式**：
```
(IsPublic == true) OR (IsPublic == false AND CreatedBy == userId)
```

**可见性边界**：

| 文档类型 | 可见条件 | 可访问用户 |
|---------|---------|-----------|
| 公开文档 | `IsPublic == true` | 所有已认证且具备 Documents.View 权限的用户 |
| 私有文档 | `IsPublic == false AND CreatedBy == userId` | 仅文档创建者本人 |

**搜索叠加逻辑**：
- 当有搜索关键词时，先按标题或描述匹配，再应用上述可见性过滤
- 即使用户知道私有文档的标题关键词，非创建者也无法通过搜索获取

---

## 四、CreatedBy 写入机制

### 4.1 CurrentUserService - 获取当前用户

[CurrentUserService](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Server/Services/CurrentUserService.cs#L9-L19)：

```csharp
public class CurrentUserService : ICurrentUserService
{
    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        UserId = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        Claims = httpContextAccessor.HttpContext?.User?.Claims.AsEnumerable()
            .Select(item => new KeyValuePair<string, string>(item.Type, item.Value)).ToList();
    }

    public string UserId { get; }
    public List<KeyValuePair<string, string>> Claims { get; set; }
}
```

**工作原理**：
- 通过 `IHttpContextAccessor` 访问当前HTTP上下文
- 从JWT Token的Claims中提取 `ClaimTypes.NameIdentifier` 作为 `UserId`
- 这是用户身份的唯一标识（通常是GUID字符串）

### 4.2 BlazorHeroContext.SaveChangesAsync - 自动写入审计字段

[BlazorHeroContext.SaveChangesAsync](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Contexts/BlazorHeroContext.cs#L35-L60)：

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
{
    foreach (var entry in ChangeTracker.Entries<IAuditableEntity>().ToList())
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Entity.CreatedOn = _dateTimeService.NowUtc;
                entry.Entity.CreatedBy = _currentUserService.UserId;  // 自动写入创建者
                break;

            case EntityState.Modified:
                entry.Entity.LastModifiedOn = _dateTimeService.NowUtc;
                entry.Entity.LastModifiedBy = _currentUserService.UserId;  // 自动写入修改者
                break;
        }
    }
    // ...
    return await base.SaveChangesAsync(cancellationToken);
}
```

**自动写入机制**：

| 操作类型 | 写入字段 | 值来源 |
|---------|---------|--------|
| 新增 (EntityState.Added) | `CreatedBy` | `_currentUserService.UserId` |
| 新增 (EntityState.Added) | `CreatedOn` | `_dateTimeService.NowUtc` |
| 修改 (EntityState.Modified) | `LastModifiedBy` | `_currentUserService.UserId` |
| 修改 (EntityState.Modified) | `LastModifiedOn` | `_dateTimeService.NowUtc` |

**关键特性**：
- **全局自动写入**：所有实现 `IAuditableEntity` 的实体都会自动应用此逻辑，无需在业务代码中手动设置
- **不可篡改**：业务层的 `AddEditDocumentCommand` 中不包含 `CreatedBy` 字段，无法通过API传入伪造值
- **值来源可靠**：`UserId` 来自认证后的Claims，而非用户输入

---

## 五、文件上传与URL命名规则

### 5.1 AddEditDocumentCommandHandler - 文件命名

[AddEditDocumentCommandHandler.Handle](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Commands/AddEdit/AddEditDocumentCommand.cs#L46-L87)：

```csharp
public async Task<Result<int>> Handle(AddEditDocumentCommand command, CancellationToken cancellationToken)
{
    var uploadRequest = command.UploadRequest;
    if (uploadRequest != null)
    {
        uploadRequest.FileName = $"D-{Guid.NewGuid()}{uploadRequest.Extension}";  // 关键命名规则
    }

    if (command.Id == 0)  // 新增
    {
        var doc = _mapper.Map<Document>(command);
        if (uploadRequest != null)
        {
            doc.URL = _uploadService.UploadAsync(uploadRequest);
        }
        await _unitOfWork.Repository<Document>().AddAsync(doc);
        await _unitOfWork.Commit(cancellationToken);
        return await Result<int>.SuccessAsync(doc.Id, _localizer["Document Saved"]);
    }
    else  // 编辑
    {
        var doc = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
        if (doc != null)
        {
            doc.Title = command.Title ?? doc.Title;
            doc.Description = command.Description ?? doc.Description;
            doc.IsPublic = command.IsPublic;
            if (uploadRequest != null)
            {
                doc.URL = _uploadService.UploadAsync(uploadRequest);  // 重新上传新文件
            }
            doc.DocumentTypeId = (command.DocumentTypeId == 0) ? doc.DocumentTypeId : command.DocumentTypeId;
            await _unitOfWork.Repository<Document>().UpdateAsync(doc);
            await _unitOfWork.Commit(cancellationToken);
            return await Result<int>.SuccessAsync(doc.Id, _localizer["Document Updated"]);
        }
        // ...
    }
}
```

### 5.2 命名规则详解

**文件名格式**：`D-{Guid}{Extension}`

**示例**：
- 原文件：`report.pdf` → 新文件名：`D-550e8400-e29b-41d4-a716-446655440000.pdf`
- 原文件：`image.png` → 新文件名：`D-123e4567-e89b-12d3-a456-426614174000.png`

**设计意图**：
1. **前缀标识**：`D-` 前缀表示Document类型文件，便于区分不同模块的上传文件
2. **全局唯一**：使用 `Guid.NewGuid()` 确保文件名全局唯一，避免文件名冲突
3. **保留扩展名**：保留原文件扩展名，确保文件类型可识别
4. **防止路径遍历**：重命名后消除了用户传入的文件名中可能包含的路径遍历字符（如 `../`）

### 5.3 UploadService - 文件存储

[UploadService.UploadAsync](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Infrastructure/Services/UploadService.cs#L10-L40)：

```csharp
public string UploadAsync(UploadRequest request)
{
    if (request.Data == null) return string.Empty;
    var streamData = new MemoryStream(request.Data);
    if (streamData.Length > 0)
    {
        var folder = request.UploadType.ToDescriptionString();
        var folderName = Path.Combine("Files", folder);
        var pathToSave = Path.Combine(Directory.GetCurrentDirectory(), folderName);
        bool exists = System.IO.Directory.Exists(pathToSave);
        if (!exists)
            System.IO.Directory.CreateDirectory(pathToSave);
        var fileName = request.FileName.Trim('"');
        var fullPath = Path.Combine(pathToSave, fileName);
        var dbPath = Path.Combine(folderName, fileName);  // 存储到数据库的相对路径
        // ... 重复名处理逻辑
        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            streamData.CopyTo(stream);
        }
        return dbPath;
    }
    // ...
}
```

**存储路径示例**：
- 完整物理路径：`{项目根目录}/Files/Documents/D-550e8400-e29b-41d4-a716-446655440000.pdf`
- 数据库存储路径（URL字段）：`Files/Documents/D-550e8400-e29b-41d4-a716-446655440000.pdf`

---

## 六、删除逻辑与扩展属性缓存处理

### 6.1 DeleteDocumentCommandHandler

[DeleteDocumentCommandHandler.Handle](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Commands/Delete/DeleteDocumentCommand.cs#L30-L51)：

```csharp
public async Task<Result<int>> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
{
    var documentsWithExtendedAttributes = _unitOfWork.Repository<Document>().Entities.Include(x => x.ExtendedAttributes);

    var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
    if (document != null)
    {
        await _unitOfWork.Repository<Document>().DeleteAsync(document);

        // 删除与该实体相关的所有缓存
        var cacheKeys = await documentsWithExtendedAttributes
            .SelectMany(x => x.ExtendedAttributes)
            .Where(x => x.EntityId == command.Id)
            .Distinct()
            .Select(x => ApplicationConstants.Cache.GetAllEntityExtendedAttributesByEntityIdCacheKey(nameof(Document), x.EntityId))
            .ToListAsync(cancellationToken);
        cacheKeys.Add(ApplicationConstants.Cache.GetAllEntityExtendedAttributesCacheKey(nameof(Document)));
        await _unitOfWork.CommitAndRemoveCache(cancellationToken, cacheKeys.ToArray());

        return await Result<int>.SuccessAsync(document.Id, _localizer["Document Deleted"]);
    }
    else
    {
        return await Result<int>.FailAsync(_localizer["Document Not Found!"]);
    }
}
```

### 6.2 扩展属性缓存处理流程

**Document实体的扩展属性**：
- Document继承自 `AuditableEntityWithExtendedAttributes`，拥有 `ExtendedAttributes` 集合属性
- 扩展属性可能被缓存以提高性能

**缓存清理步骤**：

1. **预加载关联数据**：
   ```csharp
   var documentsWithExtendedAttributes = _unitOfWork.Repository<Document>()
       .Entities.Include(x => x.ExtendedAttributes);
   ```
   使用 `Include` 预加载所有文档及其扩展属性，确保后续查询能获取完整数据。

2. **查询目标文档的扩展属性**：
   ```csharp
   var cacheKeys = await documentsWithExtendedAttributes
       .SelectMany(x => x.ExtendedAttributes)      // 展开所有扩展属性
       .Where(x => x.EntityId == command.Id)        // 过滤出目标文档的扩展属性
       .Distinct()                                  // 去重
       .Select(x => ApplicationConstants.Cache
           .GetAllEntityExtendedAttributesByEntityIdCacheKey(
               nameof(Document), x.EntityId))       // 生成按实体ID的缓存键
       .ToListAsync(cancellationToken);
   ```

3. **添加全量缓存键**：
   ```csharp
   cacheKeys.Add(ApplicationConstants.Cache
       .GetAllEntityExtendedAttributesCacheKey(nameof(Document)));
   ```
   同时删除Document类型的所有扩展属性缓存，确保缓存一致性。

4. **提交并清理缓存**：
   ```csharp
   await _unitOfWork.CommitAndRemoveCache(cancellationToken, cacheKeys.ToArray());
   ```
   在同一事务中完成数据库删除和缓存清理。

---

## 七、GetById 和 Delete 对所有者限制的复用分析

### 7.1 GetDocumentByIdQueryHandler - 按ID查询

[GetDocumentByIdQueryHandler.Handle](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Queries/GetById/GetDocumentByIdQuery.cs#L27-L33)：

```csharp
public async Task<Result<GetDocumentByIdResponse>> Handle(GetDocumentByIdQuery query, CancellationToken cancellationToken)
{
    var document = await _unitOfWork.Repository<Document>().GetByIdAsync(query.Id);
    var mappedDocument = _mapper.Map<GetDocumentByIdResponse>(document);
    return await Result<GetDocumentByIdResponse>.SuccessAsync(mappedDocument);
}
```

**关键观察**：
- **没有**使用 `DocumentFilterSpecification`
- **没有**注入 `ICurrentUserService`
- **没有**检查 `IsPublic` 或 `CreatedBy`
- 直接通过主键ID查询，返回找到的任何文档

### 7.2 DeleteDocumentCommandHandler - 删除

[DeleteDocumentCommandHandler.Handle](file:///Users/zhangjing/Desktop/so-coders/0508-und-p/blazor-starter-kit/src/Application/Features/Documents/Commands/Delete/DeleteDocumentCommand.cs#L30-L51)：

```csharp
public async Task<Result<int>> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
{
    var documentsWithExtendedAttributes = _unitOfWork.Repository<Document>().Entities.Include(x => x.ExtendedAttributes);

    var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
    if (document != null)
    {
        await _unitOfWork.Repository<Document>().DeleteAsync(document);
        // ... 缓存处理
        return await Result<int>.SuccessAsync(document.Id, _localizer["Document Deleted"]);
    }
    // ...
}
```

**关键观察**：
- **没有**使用 `DocumentFilterSpecification`
- **没有**注入 `ICurrentUserService`
- **没有**检查 `IsPublic` 或 `CreatedBy`
- 直接通过主键ID查询并删除任何文档

### 7.3 权限控制对比

| 操作 | Controller权限 | 业务层所有者检查 | 是否安全 |
|-----|---------------|-----------------|---------|
| GetAll（列表） | `[Authorize(Policy = Permissions.Documents.View)]` | ✅ DocumentFilterSpecification 过滤 | 相对安全 |
| GetById（详情） | `[Authorize(Policy = Permissions.Documents.View)]` | ❌ 无任何过滤 | **存在越权风险** |
| Post（新增/编辑） | `[Authorize(Policy = Permissions.Documents.Create)]` | ❌ 编辑时未检查创建者 | **存在越权风险** |
| Delete（删除） | `[Authorize(Policy = Permissions.Documents.Delete)]` | ❌ 无任何过滤 | **存在越权风险** |

### 7.4 安全漏洞分析

**横向越权（Horizontal Privilege Escalation）风险**：

1. **GetById 越权访问**：
   - 用户A创建了私有文档ID=5（`IsPublic=false`, `CreatedBy=UserA`）
   - 用户B（任何具备 `Documents.View` 权限的用户）调用 `GET /api/documents/5`
   - 由于没有所有者检查，用户B可以直接访问该私有文档的完整内容
   - 虽然列表查询中看不到，但通过ID直接遍历可以获取所有私有文档

2. **Delete 越权删除**：
   - 用户A创建了文档ID=5
   - 用户B（任何具备 `Documents.Delete` 权限的用户）调用 `DELETE /api/documents/5`
   - 由于没有所有者检查，用户B可以直接删除用户A的文档

3. **Post（编辑）越权修改**：
   - 用户A创建了文档ID=5
   - 用户B（任何具备 `Documents.Create` 权限的用户）调用 `POST /api/documents`，Body中 `Id=5`
   - 由于编辑时没有检查 `CreatedBy`，用户B可以修改用户A的文档内容

---

## 八、对私有文档安全测试的启发

### 8.1 安全测试重点

#### 测试点1：GetById 越权访问测试

**测试场景**：
1. 使用用户A登录，创建一个私有文档（`IsPublic=false`），记录返回的文档ID
2. 使用用户B登录（确保用户B不具备管理员权限）
3. 用户B调用 `GET /api/documents/{id}` 访问该私有文档
4. 验证：**是否返回了文档内容？**

**预期（修复后）**：应返回403 Forbidden或404 Not Found

---

#### 测试点2：ID遍历枚举测试

**测试场景**：
1. 使用任意已认证用户登录
2. 编写脚本遍历 `GET /api/documents/1` 到 `GET /api/documents/100`
3. 统计：**能够成功获取到多少个非本人创建的私有文档？**

**风险**：如果文档ID是自增整数，攻击者可以轻松枚举所有文档ID

---

#### 测试点3：Delete 越权删除测试

**测试场景**：
1. 使用用户A登录，创建一个文档，记录文档ID
2. 使用用户B登录（确保具备 `Documents.Delete` 权限）
3. 用户B调用 `DELETE /api/documents/{id}` 删除用户A的文档
4. 验证：**删除是否成功？用户A的文档是否真的被删除了？**

**预期（修复后）**：应返回403 Forbidden，文档不被删除

---

#### 测试点4：Post 越权编辑测试

**测试场景**：
1. 使用用户A登录，创建一个文档，记录文档ID
2. 使用用户B登录（确保具备 `Documents.Create` 权限）
3. 用户B调用 `POST /api/documents`，Body中设置 `Id={用户A的文档ID}` 和新的标题/内容
4. 验证：**文档内容是否被修改？修改者字段是否被更新为用户B？**

**预期（修复后）**：应返回403 Forbidden，文档内容不被修改

---

#### 测试点5：编辑时的文件覆盖测试

**测试场景**：
1. 用户A创建文档，上传文件A.pdf，URL字段为 `Files/Documents/D-GUID1.pdf`
2. 用户B越权编辑该文档，上传新文件B.pdf
3. 验证：**URL字段是否被更新？原文件A.pdf是否还存在？是否造成了文件泄露？**

---

### 8.2 修复建议

#### 建议1：GetById 和 Delete 复用 DocumentFilterSpecification

```csharp
// 修改 GetDocumentByIdQueryHandler
public async Task<Result<GetDocumentByIdResponse>> Handle(GetDocumentByIdQuery query, CancellationToken cancellationToken)
{
    var spec = new DocumentFilterSpecification("", _currentUserService.UserId);
    var document = await _unitOfWork.Repository<Document>().Entities
        .Specify(spec)
        .FirstOrDefaultAsync(d => d.Id == query.Id, cancellationToken);
    
    if (document == null)
        return await Result<GetDocumentByIdResponse>.FailAsync(_localizer["Document Not Found!"]);
    
    var mappedDocument = _mapper.Map<GetDocumentByIdResponse>(document);
    return await Result<GetDocumentByIdResponse>.SuccessAsync(mappedDocument);
}
```

#### 建议2：Delete 增加所有者检查

```csharp
// 修改 DeleteDocumentCommandHandler
public async Task<Result<int>> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
{
    var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
    if (document == null)
        return await Result<int>.FailAsync(_localizer["Document Not Found!"]);
    
    // 增加所有者检查，管理员例外
    if (document.CreatedBy != _currentUserService.UserId && !_currentUserService.IsAdmin)
        return await Result<int>.FailAsync(_localizer["You are not authorized to delete this document."]);
    
    // ... 原有删除逻辑
}
```

#### 建议3：编辑时增加所有者检查

```csharp
// 修改 AddEditDocumentCommandHandler
else // 编辑
{
    var doc = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
    if (doc == null)
        return await Result<int>.FailAsync(_localizer["Document Not Found!"]);
    
    // 增加所有者检查，管理员例外
    if (doc.CreatedBy != _currentUserService.UserId && !_currentUserService.IsAdmin)
        return await Result<int>.FailAsync(_localizer["You are not authorized to edit this document."]);
    
    // ... 原有编辑逻辑
}
```

#### 建议4：使用GUID作为文档ID

将文档的主键从自增 `int` 改为 `Guid`，增加枚举难度：

```csharp
// 修改 Document 实体
public class Document : AuditableEntityWithExtendedAttributes<Guid, int, Document, DocumentExtendedAttribute>
{
    // ...
}
```

#### 建议5：考虑添加资源级别的权限检查

使用ASP.NET Core的资源授权（Resource-based authorization）：

```csharp
// 在 Controller 中
[Authorize(Policy = Permissions.Documents.View)]
[HttpGet("{id}")]
public async Task<IActionResult> GetById(int id)
{
    var document = await _mediator.Send(new GetDocumentByIdQuery { Id = id });
    
    // 资源授权检查
    var authorizationResult = await _authorizationService.AuthorizeAsync(
        User, document.Data, "DocumentOwnerPolicy");
    
    if (!authorizationResult.Succeeded)
        return Forbid();
    
    return Ok(document);
}
```

---

### 8.3 安全测试Checklist

| 测试项 | 描述 | 预期结果 |
|-------|------|---------|
| 🔍 GetById越权 | 非所有者访问私有文档 | 403/404 |
| 🔍 Delete越权 | 非所有者删除他人文档 | 403 |
| 🔍 Edit越权 | 非所有者编辑他人文档 | 403 |
| 🔍 ID枚举 | 批量遍历文档ID | 不能获取非本人私有文档 |
| 🔍 公开文档切换 | 将文档从公开改为私有，原访问者是否还能访问 | 不能访问 |
| 🔍 缓存一致性 | 删除文档后，相关缓存是否清理 | 缓存失效 |
| 🔍 文件上传安全 | 文件命名是否可预测、是否允许危险扩展名 | GUID命名、扩展名检查 |
| 🔍 审计字段篡改 | 是否能通过API修改CreatedBy字段 | 不能修改 |
| 🔍 权限边界 | 只具备View权限的用户能否删除/修改 | 不能 |
| 🔍 管理员例外 | 管理员能否操作所有文档 | 可以（视业务需求） |

---

## 九、总结

### 9.1 当前安全状态

| 层面 | 安全控制 | 完备性 |
|-----|---------|-------|
| API入口 | [Authorize] 特性 | ✅ 已实现，阻止未认证访问 |
| 列表查询 | DocumentFilterSpecification | ✅ 已实现，正确过滤可见范围 |
| 详情查询 | 无所有者检查 | ❌ **严重风险** |
| 删除操作 | 无所有者检查 | ❌ **严重风险** |
| 编辑操作 | 无所有者检查 | ❌ **严重风险** |
| 审计字段 | SaveChangesAsync自动写入 | ✅ 已实现，防篡改 |
| 文件命名 | D-Guid规则 | ✅ 已实现，防路径遍历 |
| 缓存清理 | 删除时清理扩展属性缓存 | ✅ 已实现 |

### 9.2 核心问题

**权限控制不一致性**：列表查询使用了 `DocumentFilterSpecification` 进行严格的数据隔离，但GetById、Delete、Edit操作没有复用相同的过滤规则，导致了横向越权漏洞。

### 9.3 根本原因

1. **关注点分离过度**：权限过滤逻辑被封装在Specification中，但其他操作没有意识到需要复用
2. **缺乏资源级授权**：仅依赖Controller级别的Policy授权，没有对具体资源实例进行授权检查
3. **代码复用不足**：GetById和Delete的查询逻辑与GetAll的过滤逻辑没有统一

### 9.4 修复优先级

1. **最高优先级**：修复GetById越权访问漏洞
2. **最高优先级**：修复Delete越权删除漏洞
3. **最高优先级**：修复Edit越权修改漏洞
4. **中优先级**：使用GUID替代自增ID，降低枚举风险
5. **中优先级**：统一使用资源授权机制
6. **低优先级**：考虑添加操作日志审计

---

**文档生成时间**：2026-05-31
**分析范围**：Documents模块全流程权限控制
**结论**：当前实现存在严重的横向越权漏洞，建议立即修复GetById、Delete、Edit操作的所有者检查逻辑。
