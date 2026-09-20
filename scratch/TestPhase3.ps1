$ErrorActionPreference = "Stop"
$base = 'http://localhost:5257/api'

# 1. Login as Owner
Write-Host "`n[1] Logging in as Owner..."
$ownerLogin = @{ Email = "owner@courtbook.dev"; Password = "Owner@1234" } | ConvertTo-Json
$ownerResponse = Invoke-RestMethod -Method Post -Uri "$base/auth/login" -Body $ownerLogin -ContentType "application/json"
$ownerToken = $ownerResponse.token
$ownerHeaders = @{ Authorization = "Bearer $ownerToken"; "Content-Type" = "application/json" }
Write-Host "Owner Token acquired!"

# 2. Create Venue
Write-Host "`n[2] Owner creating a new venue..."
$venueData = @{ Name = "Test Venue"; City = "Test City"; Address = "Test Address" } | ConvertTo-Json
$venueRes = Invoke-RestMethod -Method Post -Uri "$base/venues" -Body $venueData -Headers $ownerHeaders
$venueId = $venueRes.id
Write-Host "Venue created with ID: $venueId"

# 3. Add Court
Write-Host "`n[3] Owner adding a court..."
$courtData = @{ Name = "Test Court"; SportType = "Football"; PricePerHour = 100; IsActive = $true } | ConvertTo-Json
$courtRes = Invoke-RestMethod -Method Post -Uri "$base/venues/$venueId/courts" -Body $courtData -Headers $ownerHeaders
$courtId = $courtRes.id
Write-Host "Court created with ID: $courtId"

# 4. Set Schedule
Write-Host "`n[4] Owner setting schedule for Monday (10:00 to 20:00)..."
$scheduleData = @{ DayOfWeek = 1; OpenTime = "10:00"; CloseTime = "20:00" } | ConvertTo-Json
$schedRes = Invoke-RestMethod -Method Post -Uri "$base/venues/$venueId/courts/$courtId/schedules" -Body $scheduleData -Headers $ownerHeaders
Write-Host "Schedule added!"

# 5. Login as Client
Write-Host "`n[5] Logging in as Client..."
$clientLogin = @{ Email = "client@courtbook.dev"; Password = "Client@1234" } | ConvertTo-Json
$clientResponse = Invoke-RestMethod -Method Post -Uri "$base/auth/login" -Body $clientLogin -ContentType "application/json"
$clientToken = $clientResponse.token
$clientHeaders = @{ Authorization = "Bearer $clientToken"; "Content-Type" = "application/json" }
Write-Host "Client Token acquired!"

# Determine next Monday
$today = Get-Date
$daysUntilMonday = ((1 - [int]$today.DayOfWeek) + 7) % 7
if ($daysUntilMonday -eq 0) { $daysUntilMonday = 7 }
$nextMonday = $today.AddDays($daysUntilMonday).Date

# 6. Book a slot
Write-Host "`n[6] Client booking a slot (Monday 12:00 to 14:00)..."
$startTime = $nextMonday.AddHours(12).ToString("O")
$endTime = $nextMonday.AddHours(14).ToString("O")
$bookingData = @{ CourtId = $courtId; StartTime = $startTime; EndTime = $endTime } | ConvertTo-Json
$bookRes1 = Invoke-RestMethod -Method Post -Uri "$base/bookings" -Body $bookingData -Headers $clientHeaders
$bookingId = $bookRes1.id
Write-Host "Booking successful! ID: $bookingId, Price: $($bookRes1.totalPrice)"

# 7. Try to book overlapping slot
Write-Host "`n[7] Client trying to book the SAME slot (expect 409)..."
try {
    Invoke-RestMethod -Method Post -Uri "$base/bookings" -Body $bookingData -Headers $clientHeaders | Out-Null
    Write-Host "FAIL: Overlapping booking succeeded!" -ForegroundColor Red
} catch {
    Write-Host "SUCCESS: Overlapping booking rejected with status $($_.Exception.Response.StatusCode)." -ForegroundColor Green
}

# 8. Cancel booking
Write-Host "`n[8] Client cancelling the first booking..."
Invoke-RestMethod -Method Put -Uri "$base/bookings/$bookingId/cancel" -Headers $clientHeaders | Out-Null
Write-Host "Booking cancelled!"

# 9. Book same slot again
Write-Host "`n[9] Client booking the SAME slot again after cancellation (expect success)..."
$bookRes2 = Invoke-RestMethod -Method Post -Uri "$base/bookings" -Body $bookingData -Headers $clientHeaders
Write-Host "Booking successful! ID: $($bookRes2.id)"

# 10. Book outside working hours
Write-Host "`n[10] Client booking outside working hours (Monday 08:00 to 09:00, expect 400)..."
$badStartTime = $nextMonday.AddHours(8).ToString("O")
$badEndTime = $nextMonday.AddHours(9).ToString("O")
$badBookingData = @{ CourtId = $courtId; StartTime = $badStartTime; EndTime = $badEndTime } | ConvertTo-Json
try {
    Invoke-RestMethod -Method Post -Uri "$base/bookings" -Body $badBookingData -Headers $clientHeaders | Out-Null
    Write-Host "FAIL: Booking outside hours succeeded!" -ForegroundColor Red
} catch {
    Write-Host "SUCCESS: Booking outside hours rejected with status $($_.Exception.Response.StatusCode)." -ForegroundColor Green
}
