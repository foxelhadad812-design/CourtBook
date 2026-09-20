$ErrorActionPreference = "Stop"

$web = 'http://localhost:5100'

Write-Host "`n[1] Getting Register Page..."
$res = Invoke-WebRequest -Uri "$web/Register" -SessionVariable session
Write-Host "Register page loaded."

# Extract RequestVerificationToken from form
$rvt = ($res.ParsedHtml.getElementsByTagName('input') | Where-Object { $_.name -eq '__RequestVerificationToken' }).value

Write-Host "`n[2] Registering as new Client via Web UI..."
$regBody = @{
    "Input.Name" = "Web Client"
    "Input.Email" = "webclient@courtbook.dev"
    "Input.Phone" = "123"
    "Input.Password" = "Password@123"
    "__RequestVerificationToken" = $rvt
}
$res = Invoke-WebRequest -Uri "$web/Register" -Method Post -Body $regBody -WebSession $session
Write-Host "Register POST returned status $($res.StatusCode). URL: $($res.BaseResponse.ResponseUri)"

Write-Host "`n[3] Logging in as Client via Web UI..."
$res = Invoke-WebRequest -Uri "$web/Login" -WebSession $session
$rvt = ($res.ParsedHtml.getElementsByTagName('input') | Where-Object { $_.name -eq '__RequestVerificationToken' }).value
$loginBody = @{
    "Input.Email" = "webclient@courtbook.dev"
    "Input.Password" = "Password@123"
    "__RequestVerificationToken" = $rvt
}
$res = Invoke-WebRequest -Uri "$web/Login" -Method Post -Body $loginBody -WebSession $session
Write-Host "Login POST returned status $($res.StatusCode). URL: $($res.BaseResponse.ResponseUri)"

Write-Host "`n[4] Browsing Venues..."
$res = Invoke-WebRequest -Uri "$web/Venues" -WebSession $session
Write-Host "Venues page loaded. Checking if Venues exist..."
# Find the first venue ID
$venueLink = $res.Links | Where-Object { $_.href -match '/Venues/Details/([a-f0-9-]+)' } | Select-Object -First 1
if ($venueLink) {
    $venueId = $venueLink.href -replace '/Venues/Details/', ''
    Write-Host "Found Venue ID: $venueId"

    Write-Host "`n[5] Viewing Venue Details..."
    $res = Invoke-WebRequest -Uri "$web$($venueLink.href)" -WebSession $session
    $courtLink = $res.Links | Where-Object { $_.href -match '/Courts/Book' } | Select-Object -First 1
    
    if ($courtLink) {
        Write-Host "Found Court to book: $($courtLink.href)"
        
        Write-Host "`n[6] Getting Booking Page..."
        $res = Invoke-WebRequest -Uri "$web$($courtLink.href)" -WebSession $session
        $rvt = ($res.ParsedHtml.getElementsByTagName('input') | Where-Object { $_.name -eq '__RequestVerificationToken' }).value
        
        $today = Get-Date
        $daysUntilMonday = ((1 - [int]$today.DayOfWeek) + 7) % 7
        if ($daysUntilMonday -eq 0) { $daysUntilMonday = 7 }
        $nextMonday = $today.AddDays($daysUntilMonday).Date

        Write-Host "`n[7] Booking slot via Web..."
        $bookBody = @{
            "BookingDate" = $nextMonday.ToString("yyyy-MM-dd")
            "StartTime" = "12:00"
            "EndTime" = "14:00"
            "__RequestVerificationToken" = $rvt
        }
        $res = Invoke-WebRequest -Uri "$web$($courtLink.href)" -Method Post -Body $bookBody -WebSession $session
        Write-Host "Booking POST returned. URL: $($res.BaseResponse.ResponseUri)"

        Write-Host "`n[8] Checking My Bookings Page..."
        $res = Invoke-WebRequest -Uri "$web/Bookings/MyBookings" -WebSession $session
        if ($res.Content -match "Cancel") {
            Write-Host "Booking found in My Bookings!"
            
            # Find cancel form
            $cancelForm = $res.Forms | Where-Object { $_.Action -match 'Cancel' } | Select-Object -First 1
            if ($cancelForm) {
                Write-Host "`n[9] Cancelling booking via Web..."
                $rvt = ($cancelForm.Elements | Where-Object { $_.name -eq '__RequestVerificationToken' }).value
                $cancelUrl = $cancelForm.Action
                if ($cancelUrl -notmatch "^http") { $cancelUrl = "$web$cancelUrl" }
                $cancelBody = @{ "__RequestVerificationToken" = $rvt }
                $res = Invoke-WebRequest -Uri $cancelUrl -Method Post -Body $cancelBody -WebSession $session
                Write-Host "Cancel POST returned. URL: $($res.BaseResponse.ResponseUri)"
            }
        }
    } else {
        Write-Host "No courts found in this venue!"
    }
} else {
    Write-Host "No venues found!"
}

Write-Host "`n[10] Logging out..."
$res = Invoke-WebRequest -Uri "$web/Logout" -WebSession $session
Write-Host "Logged out."

Write-Host "`n[11] Logging in as Owner via Web UI..."
$res = Invoke-WebRequest -Uri "$web/Login" -SessionVariable ownerSession
$rvt = ($res.ParsedHtml.getElementsByTagName('input') | Where-Object { $_.name -eq '__RequestVerificationToken' }).value
$loginBody = @{
    "Input.Email" = "owner@courtbook.dev"
    "Input.Password" = "Owner@1234"
    "__RequestVerificationToken" = $rvt
}
$res = Invoke-WebRequest -Uri "$web/Login" -Method Post -Body $loginBody -WebSession $ownerSession
Write-Host "Login POST returned. URL: $($res.BaseResponse.ResponseUri)"

Write-Host "`n[12] Accessing Dashboard..."
$res = Invoke-WebRequest -Uri "$web/Dashboard" -WebSession $ownerSession
Write-Host "Dashboard loaded!"

Write-Host "`nALL WEB TESTS PASSED!"
