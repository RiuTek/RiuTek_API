# RiuTek API

Hệ thống Backend API cho nền tảng thương mại điện tử linh kiện máy tính RiuTek, xây dựng trên nền tảng .NET 10 với kiến trúc Clean Architecture và CQRS (MediatR).

---

## 1. Cấu hình môi trường an toàn (Environment Secrets)

Theo chính sách bảo mật của dự án, **tuyệt đối không lưu trữ secret thật (mật khẩu database, JWT secret key, API key...) vào source code, file cấu hình được track bởi Git (`appsettings.Development.json`), hay file `appsettings.json` (kể cả khi file này đang được Git ignore)**.

Theo quy ước an toàn bắt buộc của dự án, mọi secret thật phải được cấp bằng **biến môi trường (Environment Variables)** ở runtime. Cơ chế Git ignore chỉ có tác dụng ngăn ngừa commit nhầm file lên repository, hoàn toàn không có khả năng mã hóa hay thu hồi các secret đã từng bị lộ.

---

### 1.1 Danh sách biến môi trường chính

| Tên biến môi trường | Khi nào cần | Mô tả & Quy ước |
|---|---|---|
| `JwtSettings__SecretKey` | Bắt buộc khi chạy API hoặc EF design-time tools | Chuỗi khóa bí mật ký JWT (hệ thống kiểm tra tối thiểu 32 bytes UTF-8; lưu ý độ dài chỉ là điều kiện kỹ thuật tối thiểu, khóa cần đảm bảo tính ngẫu nhiên và phức tạp cao). Tuyệt đối không dùng lại key mẫu hoặc key từng commit. |
| `ConnectionStrings__DefaultConnection` | Bắt buộc khi chạy API hoặc thao tác database thật | Chuỗi kết nối PostgreSQL (ví dụ cú pháp: `Host=localhost;Port=5432;Database=riutek_db;Username=postgres;Password=...`). |
| `RedisSettings__ConnectionString` | Chỉ cần khi `RedisSettings__Enabled=true` | Chuỗi kết nối Redis. Mặc định ở Development, Redis được tắt (`RedisSettings:Enabled = false`) nên không cần cấp. |

Các biến cấu hình không nhạy cảm liên quan:
- `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`: Xác định môi trường thực thi (ví dụ: `Development`).
- `RedisSettings__Enabled`: Bật/tắt caching Redis (`false` khi chạy local không có Redis).

---

### 1.2 Phân biệt các file cấu hình và cơ chế nạp
- **`appsettings.json`** (Bị Git bỏ qua / ignore): Là file cấu hình nền tảng (base configuration) chứa các thiết lập mặc định không nhạy cảm của ứng dụng. Đây **không** phải là nơi lưu trữ secret thật và không phải là lớp override có độ ưu tiên cao hơn các cấu hình theo môi trường.
- **`appsettings.Development.json`** (Được Git theo dõi): Chứa các cấu hình mặc định an toàn cho môi trường Development với `ConnectionStrings:DefaultConnection = ""` và placeholder cho JWT. File này override các thiết lập tương ứng từ `appsettings.json`. Tuyệt đối không lưu secret thật vào đây.
- **`.env`, `.env.*`** (Bị Git bỏ qua / ignore): .NET mặc định không tự động đọc file `.env` (dự án không cài đặt package dotenv theo thiết kế). Nếu bạn tạo file `.env` local, Git sẽ tự động ignore để phòng ngừa commit nhầm.

---

### 1.3 Cơ chế nạp cấu hình và hành vi Fail-Fast
- **Thứ tự ưu tiên cấu hình**: Lớp Infrastructure đọc giá trị từ `IConfiguration` sau khi tất cả các Configuration Providers mặc định của .NET đã được nạp và hợp nhất. Trong đó, Environment Variables Provider có độ ưu tiên cao hơn các file JSON (`appsettings.json`, `appsettings.{Environment}.json`).
- **Bản chất Fail-Fast**:
  - Guard của cơ sở dữ liệu kiểm tra giá trị cuối cùng nhận được từ `configuration.GetConnectionString("DefaultConnection")`. Nếu giá trị cuối cùng này bị thiếu, rỗng hoặc chỉ chứa khoảng trắng, ứng dụng sẽ fail-fast ném ngoại lệ yêu cầu cấu hình `ConnectionStrings__DefaultConnection`.
  - Guard của JWT kiểm tra cấu hình `JwtSettings` cuối cùng sau khi bind, yêu cầu `SecretKey` có độ dài tối thiểu 32 bytes UTF-8.
  - Code ứng dụng không kiểm tra trực tiếp sự tồn tại của biến môi trường riêng lẻ trong OS; nếu một nguồn cấu hình hợp lệ khác đã cung cấp giá trị, ứng dụng sẽ không báo lỗi. Tuy nhiên, việc cung cấp secret qua biến môi trường tại runtime là **quy ước bảo mật bắt buộc** của dự án để giảm thiểu nguy cơ lộ secret.

---

### 1.4 Hướng dẫn thiết lập môi trường an toàn khi chạy Local

Để tránh lưu credential và secret vào lịch sử dòng lệnh (command history), bạn nên thiết lập biến môi trường trong phiên (session) của terminal trước khi chạy ứng dụng từ thư mục gốc của repository (`RiuTek_API`):

#### Sử dụng PowerShell (Yêu cầu PowerShell 7.1 trở lên):
> *Lưu ý*: Tham số `-MaskInput` yêu cầu PowerShell 7.1+ (không hỗ trợ trên Windows PowerShell 5.1). Khi nhập, ký tự sẽ được che bằng ký tự thay thế (như dấu `*`).
```powershell
# Đặt môi trường rõ ràng cho phiên làm việc hiện tại
$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Nhập kín JWT Secret Key (che nội dung nhập, không lưu vào lịch sử lệnh)
$env:JwtSettings__SecretKey = Read-Host -Prompt "Nhập JwtSettings__SecretKey (tối thiểu 32 bytes UTF-8)" -MaskInput

# Nhập kín chuỗi kết nối PostgreSQL
$env:ConnectionStrings__DefaultConnection = Read-Host -Prompt "Nhập ConnectionStrings__DefaultConnection" -MaskInput

# Khởi chạy API từ thư mục gốc RiuTek_API (không dùng profile mặc định nếu cần)
dotnet run --project RiuTek.API --no-launch-profile
```

#### Lưu ý bảo mật quan trọng:
1. **Phạm vi Session & Tiến trình con**: Các lệnh thiết lập trên chỉ áp dụng cho tiến trình PowerShell hiện tại và không được lưu thành biến môi trường cấp User hay Machine. Các tiến trình con sinh ra từ terminal (như `dotnet run` hay `dotnet ef`) sẽ kế thừa các biến môi trường này. Khi đóng cửa sổ terminal, session hiện tại kết thúc, nhưng các tiến trình con còn sống (nếu có) vẫn có thể giữ giá trị đã được kế thừa.
2. **EF Core Design-Time Tools**: Lệnh EF Core (như `dotnet ef migrations has-pending-model-changes`) khi thực thi sẽ kế thừa biến môi trường từ phiên terminal đang chạy.
3. **Môi trường không phải kho mã hóa**: Biến môi trường hệ điều hành không phải là giải pháp lưu trữ mã hóa an toàn chuyên dụng; tuyệt đối không in (dump) biến môi trường hay log các giá trị secret ra console hoặc file log.
4. **Không cấu hình cấp User/Machine**: Không lưu secret vào Environment Variables cấp User/Machine của hệ điều hành hoặc PowerShell Profile vì chúng sẽ tồn tại vĩnh viễn dưới dạng văn bản rõ.
5. **Frontend Vite**: Phía frontend Vite chỉ dùng biến môi trường cho cấu hình client (như `VITE_API_BASE_URL`). Tuyệt đối không đặt server secret vào các biến `VITE_*` vì Vite sẽ đóng gói công khai các biến này xuống mã nguồn JavaScript chạy trên trình duyệt người dùng.
6. **Token người dùng**: Access token và Refresh token của người dùng là dữ liệu phiên xác thực động được tạo ở runtime, không phải biến cấu hình môi trường của hệ thống.
7. **Quy trình bổ sung secret mới**: Khi một tính năng mới cần thêm khóa bí mật/API key, người phụ trách phải thông báo tên biến, mục đích, nơi lấy và cách thiết lập cho lập trình viên; tuyệt đối không gửi hoặc yêu cầu gửi secret thật qua chat prompt.

---

### 1.5 Định hướng cấu hình runtime trên Server (Azure / VPS)
- **Azure App Service / Azure Container Apps**: Cấu hình các biến trên trong phần **Configuration > Application settings** (hoặc tích hợp an toàn với Azure Key Vault thông qua Managed Identity).
- **VPS / Docker / Kubernetes**: Cấu hình qua biến môi trường của container runtime, Docker secrets, hoặc Kubernetes Secrets gắn kết vào môi trường thực thi của container.

---

## 2. Danh mục API Sản phẩm & Danh mục (Catalog API Contracts)

### 2.1 Bảng 10 Routes, Quyền & Response Status

| Method / Route | Action | Quyền hạn | Thành công | Lỗi có thể trả về |
|---|---|---|---|---|
| `GET api/v1/products` | `GetProducts` | `[AllowAnonymous]` | `200 OK` (`PagedResult<ProductSummaryDto>`) | `400`, `404` |
| `GET api/v1/products/slug/{slug}` | `GetBySlug` | `[AllowAnonymous]` | `200 OK` (`ProductDto`) | `400`, `404` |
| `GET api/v1/products/{id:guid}` | `GetById` | `ContentManager` | `200 OK` (`ProductDto`) | `400`, `401`, `403`, `404` |
| `POST api/v1/products` | `Create` | `ContentManager` | `201 Created` (`ProductDto` + Location) | `400`, `401`, `403`, `404`, `409` |
| `PUT api/v1/products/{id:guid}` | `Update` | `ContentManager` | `200 OK` (`ProductDto`) | `400`, `401`, `403`, `404`, `409` |
| `GET api/v1/categories` | `GetTree` | `[AllowAnonymous]` | `200 OK` (`List<CategoryDto>`) | `400` |
| `GET api/v1/categories/{id:guid}` | `GetById` | `[AllowAnonymous]` | `200 OK` (`CategoryDto`) | `404` |
| `POST api/v1/categories` | `Create` | `ContentManager` | `201 Created` (`CategoryDto` + Location) | `400`, `401`, `403`, `404`, `409` |
| `PUT api/v1/categories/{id:guid}` | `Update` | `ContentManager` | `200 OK` (`CategoryDto`) | `400`, `401`, `403`, `404`, `409` |
| `DELETE api/v1/categories/{id:guid}` | `Delete` | `ContentManager` | `204 NoContent` | `400`, `401`, `403`, `404`, `409` |

#### Quy ước trạng thái & bảo mật nghiệp vụ:
- **Trạng thái `IsActive`**:
  - Là trạng thái kinh doanh (đang bán / ngừng bán), không phải quyền nhìn thấy sản phẩm.
  - `GET api/v1/products`: Mặc định `IsActive = null`, trả về cả sản phẩm đang bán và ngừng bán. Client có thể lọc bằng query param `isActive=true` hoặc `isActive=false`.
  - `GET api/v1/products/slug/{slug}`: Vẫn trả về chi tiết sản phẩm kể cả khi sản phẩm đã ngừng bán (`IsActive = false`).
  - `POST api/v1/products`: Luôn tạo sản phẩm mới ở trạng thái `IsActive = true`; request body không expose trường này.
  - `PUT api/v1/products/{id}`: Bắt buộc gửi rõ `IsActive` (kiểu boolean) trong JSON body; thiếu hoặc null sẽ bị serializer từ chối (`[property: JsonRequired]`). Cập nhật sản phẩm là full update payload, không hỗ trợ PATCH riêng status.
- **Không hỗ trợ DELETE Product**: Hệ thống không cung cấp endpoint xóa sản phẩm (không soft-delete hay hard-delete) trong scope này.
- **Xóa Category (`DELETE api/v1/categories/{id}`)**: Chỉ xóa thành công khi danh mục không có danh mục con và không chứa bất kỳ sản phẩm nào (kể cả sản phẩm ngừng bán); nếu vi phạm trả về HTTP `409 Conflict`.

---

### 2.2 Đa hình JSON thông số kỹ thuật (`ComponentSpecification`)
Hệ thống hỗ trợ 9 loại linh kiện máy tính với thuộc tính phân biệt kiểu `"$type"` đặt ở đầu object specifications:
1. `cpu` (`CpuSpecification`)
2. `motherboard` (`MotherboardSpecification`)
3. `gpu` (`GpuSpecification`)
4. `ram` (`RamSpecification`)
5. `storage` (`StorageSpecification`)
6. `psu` (`PsuSpecification`)
7. `case` (`CaseSpecification`)
8. `cooler` (`CoolerSpecification`)
9. `accessory` (`AccessorySpecification`)

#### Ví dụ truy vấn phân trang, lọc & sắp xếp (Query Parameters):
```http
GET /api/v1/products?pageIndex=1&pageSize=10&searchTerm=intel&componentType=1&minPrice=200&maxPrice=500&inStock=true&sortBy=2
```

#### Ví dụ Request Body tạo mới CPU (`POST api/v1/products`):
```json
{
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Intel Core i7-14700K",
  "sku": "CPU-INT-14700K",
  "brand": "Intel",
  "price": 420.00,
  "originalPrice": 450.00,
  "stockQuantity": 20,
  "imageUrl": "https://example.com/images/i7-14700k.jpg",
  "additionalImages": [
    "https://example.com/images/i7-box.jpg"
  ],
  "componentType": 1,
  "specifications": {
    "$type": "cpu",
    "socket": 1,
    "coreCount": 20,
    "threadCount": 28,
    "baseClockGhz": 3.4,
    "boostClockGhz": 5.6,
    "tdpWattage": 125,
    "hasIntegratedGpu": true,
    "supportedMemoryType": 2,
    "maxMemorySpeedMhz": 5600
  }
}
```

#### Ví dụ Request Body cập nhật CPU (`PUT api/v1/products/{id}`):
```json
{
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Intel Core i7-14700K",
  "sku": "CPU-INT-14700K",
  "brand": "Intel",
  "price": 399.00,
  "originalPrice": 450.00,
  "stockQuantity": 15,
  "isActive": false,
  "imageUrl": "https://example.com/images/i7-14700k.jpg",
  "additionalImages": null,
  "componentType": 1,
  "specifications": {
    "$type": "cpu",
    "socket": 1,
    "coreCount": 20,
    "threadCount": 28,
    "baseClockGhz": 3.4,
    "boostClockGhz": 5.6,
    "tdpWattage": 125,
    "hasIntegratedGpu": true,
    "supportedMemoryType": 2,
    "maxMemorySpeedMhz": 5600
  }
}
```

---

### 2.3 Giới hạn phạm vi kiểm chứng ở Phase 3.3-B3
- Các kiểm thử trong Phase 3.3-B3 tập trung vào controller contract tests (mapping, route & error metadata reflection, status/payload verification), polymorphic serializer tests (9 derived component subtypes & negative control regressions), validator tests, route ambiguity tests và endpoint metadata tests.
- **Chưa kiểm chứng qua full HTTP model-binding và middleware pipeline** (chưa chạy qua authentication handler, model-binding hay filter pipeline trên HTTP runtime server thật).
- **Chưa kiểm chứng tích hợp cơ sở dữ liệu thật** (PostgreSQL / EF SQL translation). Các nội dung này sẽ được thực hiện tại Integration Gate tiếp theo.
