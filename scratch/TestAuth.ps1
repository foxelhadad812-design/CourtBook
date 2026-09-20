$base = 'http://localhost:5257/api'

# 1. Register Client
Write-Host "Test 3: Registering client..."
$regBody = @{
    Name = "Test Client"
    Email = "test@courtbook.dev"
    Password = "Password@123"
    Phone = "123456789"
} | ConvertTo-Json
$regResponse = Invoke-RestMethod -Method Post -Uri "$base/auth/register" -Body $regBody -ContentType "application/json"
Write-Host "Register success, Token: $($regResponse.token.Substring(0, 15))..."

# 2. Login Client
Write-Host "Test 4: Logging in client..."
$loginBody = @{
    Email = "test@courtbook.dev"
    Password = "Password@123"
} | ConvertTo-Json
$loginResponse = Invoke-RestMethod -Method Post -Uri "$base/auth/login" -Body $loginBody -ContentType "application/json"
$token = $loginResponse.token
Write-Host "Login success, Role: $($loginResponse.role)"

# 3. Call Admin endpoint as Client (Should fail with 403 Forbidden because they are Client, not Admin)
Write-Host "Test 5: Call Admin endpoint as Client... expect 403"
try {
    Invoke-RestMethod -Method Get -Uri "$base/test/admin" -Headers @{ Authorization = "Bearer $token" }
} catch {
    Write-Host $_.Exception.Response.StatusCode
}

# 4. Login as seeded Admin
Write-Host "Test 6: Logging in as seeded Admin..."
$adminLogin = @{
    Email = "admin@courtbook.dev"
    Password = "Admin@1234"
} | ConvertTo-Json
$adminResponse = Invoke-RestMethod -Method Post -Uri "$base/auth/login" -Body $adminLogin -ContentType "application/json"
$adminToken = $adminResponse.token
Write-Host "Admin Login success, Role: $($adminResponse.role)"

# 5. Call Admin endpoint as Admin (Should succeed with 200)
Write-Host "Test 7: Call Admin endpoint as Admin... expect 200"
$adminTest = Invoke-RestMethod -Method Get -Uri "$base/test/admin" -Headers @{ Authorization = "Bearer $adminToken" }
Write-Host $adminTest
