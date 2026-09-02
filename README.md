# RiuTek API

Hệ thống Backend API cho nền tảng thương mại điện tử linh kiện máy tính RiuTek, xây dựng trên nền tảng .NET 10 với kiến trúc Clean Architecture và CQRS (MediatR).

---

## 1. Cấu hình môi trường an toàn (Environment Secrets)

Theo chính sách bảo mật của dự án, **không bao giờ lưu trữ secret thật (mật khẩu database, JWT secret key, API key...) vào source code, file cấu hình được track bởi Git (`appsettings.Development.json`) hay chat prompt**.

Mọi giá trị nhạy cảm được nạp vào ứng dụng thông qua **biến môi trường (Environment Variables)** ở runtime.

### 1.1 Danh sách biến môi trường chính

| Tên biến môi trường | Khi nào cần | Mô tả & Cách sử dụng |
|---|---|---|
| `JwtSettings__SecretKey` | Bắt buộc khi chạy API hoặc EF design-time tools | Chuỗi khóa bí mật ký JWT (tối thiểu 32 ký tự, đủ độ phức tạp ngẫu nhiên). Tuyệt đối không dùng lại key mẫu hoặc key từng commit. |
| `ConnectionStrings__DefaultConnection` | Bắt buộc khi chạy API hoặc thao tác database thật | Chuỗi kết nối PostgreSQL (ví dụ: `Host=localhost;Port=5432;Database=riutek_db;Username=postgres;Password=...`). |
| `RedisSettings__ConnectionString` | Chỉ cần khi `RedisSettings__Enabled=true` | Chuỗi kết nối Redis. Mặc định ở Development, Redis được tắt (`RedisSettings:Enabled = false`) nên không cần cấp. |

Các biến cấu hình không nhạy cảm liên quan:
- `ASPNETCORE_ENVIRONMENT`: Mặc định là `Development` khi phát triển local.
- `RedisSettings__Enabled`: Mặc định `false` khi chạy local không có Redis.

---

### 1.2 Phân biệt các file cấu hình
- **`appsettings.Development.json`** (Được Git theo dõi): Chỉ chứa cấu hình mặc định an toàn cho môi trường Development với `ConnectionStrings:DefaultConnection = ""` và placeholder cho JWT. Không lưu mật khẩu/key thật vào đây.
- **`appsettings.json`** (Bị Git bỏ qua / ignore): Dành cho cấu hình production hoặc local override riêng tư, không được commit hay push lên repo.
- **`.env`, `.env.*`** (Bị Git bỏ qua / ignore): Không tự động được nạp bởi .NET (dự án không cài đặt package dotenv theo thiết kế). Nếu bạn tạo file `.env` local, Git sẽ tự động ignore để bảo vệ secret.

---

### 1.3 Hướng dẫn thiết lập môi trường an toàn khi chạy Local

Để tránh lưu credential vào lịch sử command line, bạn nên nhập kín giá trị vào phiên terminal hiện tại:

#### Sử dụng PowerShell (Khuyến nghị):
```powershell
# Nhập kín JWT Secret Key (không hiển thị ký tự và không lưu vào lịch sử lệnh)
$env:JwtSettings__SecretKey = Read-Host -Prompt "Nhập JwtSettings__SecretKey (>= 32 ký tự)" -MaskInput

# Nhập kín chuỗi kết nối PostgreSQL
$env:ConnectionStrings__DefaultConnection = Read-Host -Prompt "Nhập ConnectionStrings__DefaultConnection" -MaskInput

# Khởi chạy API
dotnet run --project RiuTek.API
```

#### Lưu ý quan trọng:
1. **Phạm vi Process/Session**: Các biến môi trường thiết lập như trên chỉ tồn tại trong phiên terminal hiện tại và sẽ tự động xóa sạch khi bạn đóng cửa sổ terminal.
2. **EF Core Design-Time Tools**: Lệnh EF Core (như `dotnet ef migrations has-pending-model-changes`) khi khởi chạy startup project sẽ kế thừa biến môi trường của terminal đang chạy.
3. **Fail-Fast**: Nếu thiếu `ConnectionStrings__DefaultConnection` hoặc `JwtSettings__SecretKey`, ứng dụng sẽ fail-fast ngay khi khởi động và báo rõ tên biến cần bổ sung. Đây là hành vi bảo vệ có chủ đích.
4. **Bảo vệ Secret**: Không in (dump) biến môi trường hay log các giá trị bí mật ra màn hình.
5. **Frontend Vite**: Phía frontend Vite chỉ dùng biến môi trường cho cấu hình client (như `VITE_API_BASE_URL`). Không đặt server secret vào các biến `VITE_*` vì các biến này được đóng gói công khai xuống trình duyệt người dùng.
6. **Token người dùng**: Access token và Refresh token của người dùng là dữ liệu xác thực động được tạo ở runtime, không phải biến môi trường của hệ thống.

---

### 1.4 Định hướng triển khai trên Server (Azure / VPS)
- Trên Azure App Service / Container Apps: Cấu hình các biến trên trong phần **Configuration > Application settings** (hoặc liên kết Azure Key Vault).
- Trên VPS / Docker: Cấu hình qua Docker environment variables hoặc secret management của nền tảng điều phối (Kubernetes / Docker Swarm / systemd service environment file).
